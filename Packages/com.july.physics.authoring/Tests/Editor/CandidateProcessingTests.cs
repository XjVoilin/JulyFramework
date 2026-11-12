using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.Physics.Authoring
{
    public sealed class CandidateProcessingTests
    {
        const string Folder = "Assets/CandidateProcessingTests";
        string path, rootName;
        ProcessingAdapter adapter;
        ColliderWorkspace workspace;
        ColliderRow Row => workspace.Items.Single();

        sealed class Processor : IColliderCandidateProcessor
        {
            readonly Action<ColliderCandidateDraft> edit;
            public string Fingerprint { get; }
            internal int Calls;
            internal ColliderCandidateDraft Retained;
            internal Processor(string fingerprint, Action<ColliderCandidateDraft> edit)
            { Fingerprint = fingerprint; this.edit = edit; }
            public void Process(ColliderCandidateDraft candidate)
            { Calls++; Retained = candidate; edit(candidate); }
        }
        sealed class ProcessingAdapter : IColliderAuthoringAdapter
        {
            readonly DefaultColliderAuthoringAdapter inner = new DefaultColliderAuthoringAdapter();
            readonly string rootName;
            internal IColliderCandidateProcessor Processor;
            internal ProcessingAdapter(string rootName) { this.rootName = rootName; }
            public ColliderAuthoringProfile CreateProfile() => new ColliderAuthoringProfile("candidate-processing-tests", "Processing", new[]
            {
                new ColliderOutputTarget("physical", "Envelope", "PhysicalShapes", ColliderGeometryGoal.Envelope, true)
            }, editableTargets: false);
            public bool CanHandle(GameObject root) => root.name == rootName;
            public ColliderAssetLayout Inspect(GameObject root, ColliderAuthoringProfile profile)
            {
                var layout = inner.Inspect(root, profile); layout.Targets[0].Processor = Processor; return layout;
            }
            public void Prepare(GameObject root, ColliderAuthoringProfile profile, System.Collections.Generic.IReadOnlyList<string> ids) => inner.Prepare(root, profile, ids);
            public ColliderGenerationRequest CreateRequest(GameObject root, ColliderAuthoringProfile profile, string id) => inner.CreateRequest(root, profile, id);
            public void Complete(GameObject root, System.Collections.Generic.IReadOnlyList<ColliderTargetResult> results) { }
        }

        [SetUp] public void Setup()
        {
            Directory.CreateDirectory(Folder);
            rootName = "processing-" + Guid.NewGuid().ToString("N"); path = Folder + "/" + rootName + ".prefab";
            var root = new GameObject(rootName);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(root.transform, false); cube.transform.localScale = new Vector3(2, 1, 1);
            var group = new GameObject("PhysicalShapes"); group.transform.SetParent(root.transform, false);
            group.AddComponent<BoxCollider>().size = new Vector3(2, 1, 1);
            PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root);
            adapter = new ProcessingAdapter(rootName);
            workspace = new ColliderWorkspace(adapter, "Library/ProcessingTests/" + rootName + ".json");
            workspace.Read(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) });
        }
        [TearDown] public void Cleanup() { workspace.Dispose(); AssetDatabase.DeleteAsset(Folder); }
        static IEnumerator Await(Task task)
        { while (!task.IsCompleted) yield return null; if (task.IsFaulted) throw task.Exception; }
        IEnumerator Generate() => Await(workspace.GenerateAsync(new[] { Row }, ColliderTargetSelection.All));

        [UnityTest] public IEnumerator ProcessedResultIsSealedCachedAndAppliedWithoutRunningAgain()
        {
            var processor = new Processor("offset/v1/3", draft =>
            {
                foreach (var shape in draft.Shapes) shape.Center += Vector3.right * 3;
                draft.Summary += "\nproject offset";
            });
            adapter.Processor = processor;
            yield return Generate(); Assert.That(Row.Errors, Is.Empty);
            var preview = Row.ActiveCandidates[0].Preview;
            Assert.That(preview.Summary, Does.Contain("project offset"));
            var mesh = ColliderGeneration.CreatePreviewMesh(preview, Matrix4x4.identity);
            Assert.That(mesh.bounds.center.x, Is.EqualTo(3).Within(.0001)); Object.DestroyImmediate(mesh);
            string hash = preview.ContentHash;
            processor.Retained.Shapes[0].Center = Vector3.one * 100;
            Assert.That(preview.ContentHash, Is.EqualTo(hash), "Retained drafts cannot mutate sealed previews.");
            var loaded = ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(preview));
            Assert.That(loaded.ContentHash, Is.EqualTo(hash)); Assert.That(loaded.MatchesProcessor(processor));
            workspace.Apply(new[] { Row }, ColliderTargetSelection.All);
            workspace.Apply(new[] { Row }, ColliderTargetSelection.All);
            Assert.That(processor.Calls, Is.EqualTo(1));
            var collider = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<BoxCollider>();
            Assert.That(collider.transform.localPosition.x, Is.EqualTo(3).Within(.0001));
            using var reopened = new ColliderWorkspace(adapter, "Library/ProcessingTests/" + rootName + ".json");
            reopened.Read(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) });
            Assert.That(reopened.Items.Single().HasCurrentCandidate("physical"));
            Assert.That(reopened.Items.Single().ActiveCandidates[0].Preview.ContentHash, Is.EqualTo(hash));
            Assert.That(processor.Calls, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator ChangedOrRemovedProcessorExpiresGeneratedCandidates()
        {
            adapter.Processor = new Processor("rule/v1", _ => { }); yield return Generate();
            var row = Row; Assert.That(row.HasCurrentCandidate("physical"));
            adapter.Processor = new Processor("rule/v2", _ => { }); workspace.RefreshCurrent();
            Assert.That(row.HasCurrentCandidate("physical"), Is.False);
            byte[] before = File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(() => ColliderWorkspace.ApplyRow(row, ColliderTargetSelection.All));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            adapter.Processor = null; workspace.RefreshCurrent();
            Assert.That(row.HasCurrentCandidate("physical"), Is.False);
        }

        [UnityTest] public IEnumerator FailedProcessingRetainsLastSuccessfulCandidate()
        {
            yield return Generate(); var previous = Row.ActiveCandidates[0].Preview;
            adapter.Processor = new Processor("failure/v1", _ => throw new InvalidOperationException("policy failed"));
            LogAssert.Expect(LogType.Error, new Regex("policy failed")); yield return Generate();
            Assert.That(Row.ActiveCandidates[0].Preview, Is.SameAs(previous));
            Assert.That(Row.ActiveCandidates[0].Progress, Is.EqualTo(ColliderProgress.Failed));
        }

        [UnityTest] public IEnumerator InvalidProcessedGeometryIsRejectedBeforeCachingOrApplication()
        {
            yield return Generate(); string hash = Row.ActiveCandidates[0].Preview.ContentHash;
            adapter.Processor = new Processor("invalid/v1", draft => draft.Shapes[0].BoxSize = Vector3.zero);
            LogAssert.Expect(LogType.Error, new Regex("候选盒体尺寸无效")); yield return Generate();
            Assert.That(Row.ActiveCandidates[0].Preview.ContentHash, Is.EqualTo(hash));
            Assert.That(Row.ActiveCandidates[0].Progress, Is.EqualTo(ColliderProgress.Failed));
        }

        [UnityTest] public IEnumerator ManualReuseBypassesProcessingAndPolicyChanges()
        {
            var processor = new Processor("manual/v1", _ => throw new InvalidOperationException("must not process"));
            adapter.Processor = processor; workspace.RefreshCurrent();
            workspace.ReuseCurrentEnvelope(Row, "physical");
            adapter.Processor = new Processor("manual/v2", _ => throw new InvalidOperationException("must not process"));
            workspace.RefreshCurrent(); Assert.That(Row.HasCurrentCandidate("physical"));
            workspace.Apply(new[] { Row }, ColliderTargetSelection.All);
            Assert.That(processor.Calls, Is.Zero);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<BoxCollider>().size, Is.EqualTo(new Vector3(2, 1, 1)));
            yield return null;
        }

        [UnityTest] public IEnumerator DirectAuthoringEntryUsesTheInspectedProcessor()
        {
            var processor = new Processor("direct/v1", draft => draft.Shapes[0].Center += Vector3.up);
            adapter.Processor = processor;
            ColliderAuthoringAdapters.Register(rootName, () => adapter);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var context = ColliderAuthoringContext.Create(root.transform.Find("PhysicalShapes"), ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Envelope));
                Assert.That(context.Request.Processor, Is.SameAs(processor));
                var pending = ColliderGeneration.PreviewAsync(context.Request); yield return Await(pending);
                var preview = ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(pending.Result));
                var result = ColliderGeneration.ApplyPreview(ColliderGeneration.RetargetPreview(preview, context.Request));
                Assert.That(result.Colliders.Single().transform.localPosition.y, Is.EqualTo(1).Within(.0001));
                Assert.That(processor.Calls, Is.EqualTo(1));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [UnityTest] public IEnumerator OlderUnprocessedSnapshotsRemainUsableWithoutAPolicy()
        {
            yield return Generate();
            string json = ColliderGeneration.SerializePreview(Row.ActiveCandidates[0].Preview).Replace(",\"processorFingerprint\":\"\"", "");
            var loaded = ColliderGeneration.DeserializePreview(json);
            Assert.That(loaded.MatchesProcessor(null));
            Assert.That(loaded.MatchesProcessor(new Processor("added/v1", _ => { })), Is.False);
        }

        [UnityTest] public IEnumerator ProcessingCancellationPreservesPreviousCandidateAndReleasesQueue()
        {
            yield return Generate(); var previous = Row.ActiveCandidates[0].Preview;
            adapter.Processor = new Processor("cancel/v1", _ => throw new OperationCanceledException());
            yield return Generate();
            Assert.That(workspace.Busy, Is.False);
            Assert.That(Row.ActiveCandidates[0].Progress, Is.EqualTo(ColliderProgress.Cancelled));
            Assert.That(Row.ActiveCandidates[0].Preview, Is.SameAs(previous));
        }

        [Test] public void ProcessorWithoutStableFingerprintIsRejectedBeforeFitting()
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var processor = new Processor(" ", _ => { });
                var request = ColliderGeneration.CreateRequest(root.transform.Find("PhysicalShapes"), ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Envelope));
                request.Sources = ColliderGeometry.Scan(root.transform); request.Processor = processor;
                var pending = ColliderGeneration.PreviewAsync(request);
                Assert.That(pending.IsFaulted);
                Assert.That(pending.Exception.InnerException, Is.TypeOf<ArgumentException>());
                Assert.That(processor.Calls, Is.Zero);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        [Test] public void BadgeWarningClearsWhenVirtualizedRowIsRebound()
        {
            var window = ScriptableObject.CreateInstance<ColliderOverviewWindow>();
            window.CreateGUI();
            var view = new ColliderRowView(window);
            try
            {
                var profile = window.Workspace.Profile;
                ColliderRow Row(string badge, bool warning) => new ColliderRow
                {
                    Owner = window.Workspace, Path = "Assets/item.prefab",
                    Inspection = new ColliderInspection(profile.Targets.Length)
                    {
                        Layout = new ColliderAssetLayout { Badge = badge, BadgeWarning = warning,
                            Targets = new[] { new ColliderTargetLayout() } },
                        Error = "test"
                    }
                };
                var missing = Row("尺码：未配置", true);
                missing.Inspection.Current[0] = new ColliderCurrent();
                window.Workspace.Rows.Add(missing); window.Refresh();
                view.Bind(missing);
                var label = view.Root.Q<Label>("collider-item-badge");
                Assert.That(label.text, Is.EqualTo("尺码：未配置"));
                Assert.That(label.ClassListContains("collider-badge-warning"), Is.True);
                var configured = Row("尺码：M", false);
                configured.Inspection.Current[0] = new ColliderCurrent();
                window.Workspace.Rows.Add(configured); window.Refresh();
                view.Bind(configured);
                Assert.That(label.text, Is.EqualTo("尺码：M"));
                Assert.That(label.ClassListContains("collider-badge-warning"), Is.False);
            }
            finally { view.Release(); Object.DestroyImmediate(window); }
        }
    }
}
