using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.Physics.Authoring
{
    public sealed class ColliderWorkbenchTests
    {
        const string Folder = "Assets/WorkbenchTest";
        const string ViewFile = "Library/July/ColliderAuthoring/default/view.json";
        string path, settings, viewState;
        ColliderWorkspace workspace;
        [SetUp] public void Setup()
        {
            settings = File.Exists(ColliderAuthoringSettings.FileName) ? File.ReadAllText(ColliderAuthoringSettings.FileName) : null;
            if (settings != null) File.Delete(ColliderAuthoringSettings.FileName);
            viewState = File.Exists(ViewFile) ? File.ReadAllText(ViewFile) : null;
            if (viewState != null) File.Delete(ViewFile);
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var root = new GameObject("item"); root.layer = 9;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.name = "Visual"; visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(.7f, 1.1f, .45f);
            path = Folder + "/" + Guid.NewGuid().ToString("N") + ".prefab"; PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root);
            workspace = new ColliderWorkspace(); workspace.Read(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) });
        }
        [TearDown] public void Cleanup()
        {
            workspace.Dispose(); AssetDatabase.DeleteAsset(Folder);
            if (settings == null) { if (File.Exists(ColliderAuthoringSettings.FileName)) File.Delete(ColliderAuthoringSettings.FileName); }
            else File.WriteAllText(ColliderAuthoringSettings.FileName, settings);
            if (viewState == null) { if (File.Exists(ViewFile)) File.Delete(ViewFile); }
            else File.WriteAllText(ViewFile, viewState);
        }
        static IEnumerator Await(Task t) { while (!t.IsCompleted) yield return null; if (t.IsFaulted) throw t.Exception; }
        [Test] public void PerformanceSettingsAreDetachedPersistedAndSeparatedByProfile()
        {
            var draft=workspace.Configuration;
            draft.Performance.EnvelopeReminder=1;draft.Performance.EnvelopeAttention=2;
            draft.Performance.Enabled=false;
            Assert.That(workspace.Configuration.Performance.EnvelopeReminder,Is.EqualTo(3));
            workspace.Configure(draft);
            using(var reopened=new ColliderWorkspace())
            {
                Assert.That(reopened.Configuration.Performance.EnvelopeReminder,Is.EqualTo(1));
                Assert.That(reopened.Configuration.Performance.EnvelopeAttention,Is.EqualTo(2));
                Assert.That(reopened.Configuration.Performance.Enabled,Is.False);
            }
            var other=new ColliderAuthoringProfile("performance-other","Other",draft.Targets,editableTargets:false);
            ColliderAuthoringSettings.Load(other);Assert.That(other.Performance.EnvelopeReminder,Is.EqualTo(3));
            other.Performance.TotalVerticesReminder=32;ColliderAuthoringSettings.Save(other);
            other=new ColliderAuthoringProfile("performance-other","Other",draft.Targets,editableTargets:false);
            ColliderAuthoringSettings.Load(other);Assert.That(other.Performance.TotalVerticesReminder,Is.EqualTo(32));
            workspace.ReloadConfiguration();Assert.That(workspace.Configuration.Performance.EnvelopeReminder,Is.EqualTo(1));
        }
        [Test] public void LegacyConfigurationWithoutPerformanceUsesDefaults()
        {
            workspace.Configure(workspace.Configuration);
            string json=File.ReadAllText(ColliderAuthoringSettings.FileName);
            json=System.Text.RegularExpressions.Regex.Replace(json,",\\s*\"performance\"\\s*:\\s*\\{[^}]*\\}","");
            Assert.That(json,Does.Not.Contain("performance"));File.WriteAllText(ColliderAuthoringSettings.FileName,json);
            workspace.ReloadConfiguration();var settings=workspace.Configuration.Performance;
            Assert.That(settings.Enabled);Assert.That(settings.EnvelopeReminder,Is.EqualTo(3));
            Assert.That(settings.HullVerticesReminder,Is.EqualTo(16));Assert.That(settings.TotalVerticesAttention,Is.EqualTo(128));
        }
        [Test] public void InvalidPerformanceThresholdsDoNotWriteConfiguration()
        {
            var draft=workspace.Configuration;draft.Performance.ConvexReminder=-1;
            Assert.Throws<ArgumentException>(()=>workspace.Configure(draft));
            draft.Performance.ConvexReminder=9;draft.Performance.ConvexAttention=8;
            Assert.Throws<ArgumentException>(()=>workspace.Configure(draft));
            Assert.That(File.Exists(ColliderAuthoringSettings.FileName),Is.False);
        }
        [UnityTest] public IEnumerator PerformanceEditsReassessWithoutInvalidatingCandidateOrReview()
        {
            var row=workspace.Items.Single();yield return Await(workspace.GenerateAsync(new[]{row},ColliderTargetSelection.All));
            Assert.That(row.Errors,Is.Empty);Assert.That(row.CanApply(0));
            workspace.Apply(new[]{row},ColliderTargetSelection.All);
            workspace.MarkHandled(row);string hash=row.Candidates[0].Preview.ContentHash;
            byte[] prefab=File.ReadAllBytes(path);
            var draft=workspace.Configuration;draft.Performance.StructureReminder=0;draft.Performance.StructureAttention=0;
            workspace.Configure(draft);row=workspace.Items.Single();
            Assert.That(row.CanApply(0));Assert.That(row.IsConfirmed);
            Assert.That(row.Candidates[0].Preview.ContentHash,Is.EqualTo(hash));
            Assert.That(ColliderPerformance.Candidate(row.Candidates[0].Preview,workspace.Profile.Performance).Level,Is.EqualTo(ColliderPerformanceLevel.Attention));
            CollectionAssert.AreEqual(prefab,File.ReadAllBytes(path));
        }
        [Test] public void SettingsNamedProfileKeepsReviewSeparateFromSharedSettings()
        {
            var profile = new ColliderAuthoringProfile("settings", "Settings", workspace.Configuration.Targets);
            profile.Validate();
            Assert.That(profile.ReviewFile, Is.Not.EqualTo(ColliderAuthoringSettings.FileName));
            string review = File.Exists(profile.ReviewFile) ? File.ReadAllText(profile.ReviewFile) : null;
            try
            {
                ColliderAuthoringSettings.Save(profile);
                var before = File.ReadAllBytes(ColliderAuthoringSettings.FileName);
                new ColliderReviews(profile).Save();
                CollectionAssert.AreEqual(before, File.ReadAllBytes(ColliderAuthoringSettings.FileName));
                Assert.That(JsonUtility.FromJson<Version>(File.ReadAllText(profile.ReviewFile)).version, Is.EqualTo(2));
            }
            finally
            {
                if (review == null) File.Delete(profile.ReviewFile); else File.WriteAllText(profile.ReviewFile, review);
            }
        }
        [Serializable] sealed class Version { public int version; }
        [Test] public void SharedCandidateDirectoryIsSeparateFromPersonalModeSelection()
        {
            Assert.That(workspace.Profile.CacheDirectory, Is.EqualTo("July/ColliderAuthoring"));
            workspace.ChangeGoal("default", ColliderGeometryGoal.Envelope);
            Assert.IsTrue(File.Exists(ViewFile));
            Assert.IsFalse(File.Exists(Path.Combine(workspace.Profile.CacheDirectory, "view.json")));
        }

        [Test] public void ModeSelectionDoesNotWriteSharedConfiguration()
        {
            var config = workspace.Configuration;
            workspace.Configure(config);
            var before = File.ReadAllBytes(ColliderAuthoringSettings.FileName);
            workspace.ChangeGoal("default", ColliderGeometryGoal.Envelope);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(ColliderAuthoringSettings.FileName));
            workspace.ReloadConfiguration();
            Assert.That(workspace.Configuration.Targets[0].Settings.Goal, Is.EqualTo(ColliderGeometryGoal.Envelope));
            File.Delete(ViewFile); workspace.ReloadConfiguration();
            Assert.That(workspace.Configuration.Targets[0].Settings.Goal, Is.EqualTo(ColliderGeometryGoal.Structure));
        }
        [Test] public void DefaultHasOneOutputAndDraftIsDetached()
        {
            var config = workspace.Configuration;
            Assert.That(config.Targets.Length, Is.EqualTo(1)); Assert.That(config.Targets[0].Path, Is.EqualTo("Colliders"));
            config.Targets[0].Path = "Changed"; Assert.That(workspace.Configuration.Targets[0].Path, Is.EqualTo("Colliders"));
            Assert.That(workspace.Items.Single().IsNew); Assert.That(workspace.Items.Single().Current.Single().Count, Is.Zero);
        }
        [Test] public void InvalidOverlappingOutputPathsAreRejectedBeforeConfigSave()
        {
            var draft = workspace.Configuration;
            draft.Targets = new[] { draft.Targets[0], new ColliderOutputTarget("second", "第二组", "Colliders/Nested", ColliderGeometryGoal.Envelope) };
            Assert.Throws<ArgumentException>(() => workspace.Configure(draft)); Assert.That(File.Exists(ColliderAuthoringSettings.FileName), Is.False);
        }
        void ConfigureEnvelopeAndPicking()
        {
            var config = workspace.Configuration;
            config.Targets[0].ChangeGoal(ColliderGeometryGoal.Envelope);
            config.Targets = config.Targets.Concat(new[] {
                new ColliderOutputTarget("picking", "结构贴合", "Picking", ColliderGeometryGoal.Structure) }).ToArray();
            workspace.Configure(config);
        }
        [UnityTest] public IEnumerator ReusedEnvelopeIsDetachedCachedAndAppliesOnlyToPicking()
        {
            ConfigureEnvelopeAndPicking();
            var row = workspace.Items.Single(); var before = File.ReadAllBytes(path);
            Assert.That(workspace.CanReuseEnvelopePreview(row,"default","picking"),Is.False);
            yield return Await(workspace.GenerateAsync(new[]{row},ColliderTargetSelection.Only("default")));
            Assert.That(row.Errors,Is.Empty);
            var source = row.Candidates[0].Preview; string original = ColliderGeneration.SerializePreview(source);
            Assert.That(workspace.CanReuseEnvelopePreview(row,"default","picking"));
            workspace.ReuseEnvelopePreview(row,"default","picking");
            var copied = row.Candidates[1].Preview;
            Assert.That(copied.Goal,Is.EqualTo(ColliderGeometryGoal.Structure));
            Assert.That(copied.Fit,Is.Not.SameAs(source.Fit));
            Assert.That(copied.Fit.Shapes,Is.Not.SameAs(source.Fit.Shapes));
            for(int i=0;i<source.Count;i++)
            {
                Assert.That(copied.Fit.Shapes[i],Is.Not.SameAs(source.Fit.Shapes[i]));
                Assert.That(JsonUtility.ToJson(copied.Fit.Shapes[i]),Is.EqualTo(JsonUtility.ToJson(source.Fit.Shapes[i])));
            }
            Assert.That(row.CanApply(1)); Assert.That(row.Candidates[1].Applied,Is.False);
            Assert.That(row.IsConfirmed,Is.False);
            Assert.That(ColliderGeneration.SerializePreview(source),Is.EqualTo(original));
            CollectionAssert.AreEqual(before,File.ReadAllBytes(path));
            string copiedHash = copied.ContentHash;
            workspace.Read(new Object[]{AssetDatabase.LoadAssetAtPath<GameObject>(path)}); row=workspace.Items.Single();
            Assert.That(row.Candidates[1].Preview.ContentHash,Is.EqualTo(copiedHash));
            Assert.That(row.Candidates[1].Preview.Summary,Does.Contain("手动复用"));
            Assert.That(row.CanApply(1));
            workspace.Apply(new[]{row},ColliderTargetSelection.Only("picking"));
            Assert.That(row.Errors,Is.Empty);
            Assert.That(row.Current[0].Count,Is.Zero);
            Assert.That(row.Current[1].NativeComponents,Is.EqualTo(source.Count));
            Assert.That(row.Candidates[0].Applied,Is.False);
            Assert.That(row.Candidates[1].Applied);
            CollectionAssert.AreEquivalent(source.Fit.Shapes.Select(x=>x.Type),row.Candidates[1].Preview.Fit.Shapes.Select(x=>x.Type));
        }
        [UnityTest] public IEnumerator ReuseIgnoresGenerationBudgetAndRetainsCopiedShapeCount()
        {
            ConfigureEnvelopeAndPicking();var row=workspace.Items.Single();
            yield return Await(workspace.GenerateAsync(new[]{row},ColliderTargetSelection.Only("default")));
            workspace.ReuseEnvelopePreview(row,"default","picking");
            workspace.SetGenerationSettings(row,"picking",1,8);
            var source=row.Candidates[0].Preview;var pieces=source.Fit.Shapes;
            source.Fit.Shapes=pieces.Concat(pieces).ToArray();
            Assert.That(workspace.CanReuseEnvelopePreview(row,"default","picking"));
            workspace.ReuseEnvelopePreview(row,"default","picking");
            Assert.That(row.Candidates[1].Preview.Count,Is.EqualTo(pieces.Length*2));
            Assert.That(row.CanApply(1));
            workspace.SetGenerationSettings(row,"picking",1,8,maxHullVertices:4);
            Assert.That(row.CanApply(1),"Generation settings do not invalidate manually copied shapes.");
        }
        [UnityTest] public IEnumerator ReuseButtonDirectlyCopiesReadyEnvelopeWithoutOpeningMenu()
        {
            ConfigureEnvelopeAndPicking();
            var window=ScriptableObject.CreateInstance<ColliderOverviewWindow>(); window.Show();
            try
            {
                window.Workspace.Read(new Object[]{AssetDatabase.LoadAssetAtPath<GameObject>(path)});
                for(int i=0;i<8;i++)yield return null;
                var envelope=window.rootVisualElement.Q<Button>("collider-reuse-envelope-0");
                var structure=window.rootVisualElement.Q<Button>("collider-reuse-envelope-1");
                Assert.That(envelope.resolvedStyle.display,Is.EqualTo(DisplayStyle.None));
                Assert.That(structure.resolvedStyle.display,Is.EqualTo(DisplayStyle.Flex));
                Assert.That(structure.enabledSelf,Is.False);
                var row=window.Workspace.Items.Single();
                yield return Await(window.Workspace.GenerateAsync(new[]{row},ColliderTargetSelection.Only("default")));
                Assert.That(row.Errors,Is.Empty);
                Assert.That(structure.enabledSelf);
                Assert.That(row.Candidates[1].Preview,Is.Null);
                using(var submit=NavigationSubmitEvent.GetPooled()) { submit.target=structure;structure.SendEvent(submit); }
                Assert.That(row.Candidates[1].Preview,Is.Not.Null);
                Assert.That(row.Candidates[1].Preview.Count,Is.EqualTo(row.Candidates[0].Preview.Count));
                Assert.That(row.CanApply(1));
            }
            finally { window.Close(); }
        }
        void AuthorCurrentEnvelope(bool mixed = false)
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var group=new GameObject("Colliders");group.transform.SetParent(root.transform,false);
                group.transform.localScale=new Vector3(2,3,.5f);
                group.transform.localPosition=new Vector3(.3f,-.2f,.4f);
                group.transform.localRotation=Quaternion.Euler(17,31,-9);
                GameObject Node(string name,Vector3 position)
                { var go=new GameObject(name);go.transform.SetParent(group.transform,false);go.transform.localPosition=position;return go; }
                var box=Node("Box",new Vector3(.1f,.2f,.3f)).AddComponent<BoxCollider>();
                box.center=new Vector3(.2f,-.1f,.3f);box.size=new Vector3(.3f,.8f,.5f);
                box.transform.localRotation=Quaternion.Euler(0,90,0);
                if(mixed)
                {
                    var sphere=Node("Sphere",new Vector3(-.5f,.1f,.2f)).AddComponent<SphereCollider>();
                    sphere.center=new Vector3(.1f,.2f,-.1f);sphere.radius=.17f;
                    foreach(int axis in new[]{0,2})
                    {
                        var cap=Node("Capsule"+axis,new Vector3(.2f,axis*.15f,-.4f)).AddComponent<CapsuleCollider>();
                        cap.center=new Vector3(-.1f,.3f,.2f);cap.radius=.1f;cap.height=.9f;cap.direction=axis;
                    }
                }
                var disabled=Node("Disabled",Vector3.zero).AddComponent<BoxCollider>();disabled.size=Vector3.one*100;disabled.enabled=false;
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            workspace.RefreshCurrent();
        }
        [Test] public void CurrentEnvelopeRoundTripPreservesNativeShapesWithoutFittingOrWritingUntilApply()
        {
            ConfigureEnvelopeAndPicking();AuthorCurrentEnvelope(true);
            var row=workspace.Items.Single();var before=File.ReadAllBytes(path);
            workspace.SetGenerationSettings(row,"default",1,8);
            var originalMesh=ColliderGeneration.CreateCurrentPreviewMesh(row.Current[0].Shapes,Matrix4x4.identity,false);
            try
            {
                Assert.That(workspace.CanReuseCurrentEnvelope(row,"default"));
                workspace.ReuseCurrentEnvelope(row,"default");
                Assert.That(row.Candidates[0].Preview.Count,Is.EqualTo(4),"Manual capture preserves authored count, even above the fitting target.");
                Assert.That(row.Candidates[0].Preview.NativeCount,Is.EqualTo(4));
                Assert.That(row.Candidates[0].Preview.Summary,Does.Contain("当前"));
                Assert.That(row.Candidates[1].Preview,Is.Null);
                Assert.That(row.Candidates[0].Applied,Is.False);Assert.That(row.IsConfirmed,Is.False);
                CollectionAssert.AreEqual(before,File.ReadAllBytes(path));
                string hash=row.Candidates[0].Preview.ContentHash;
                workspace.Read(new Object[]{AssetDatabase.LoadAssetAtPath<GameObject>(path)});row=workspace.Items.Single();
                Assert.That(row.Candidates[0].Preview.ContentHash,Is.EqualTo(hash));Assert.That(row.CanApply(0));
                workspace.Apply(new[]{row},ColliderTargetSelection.Only("default"));
                Assert.That(row.Errors,Is.Empty);Assert.That(row.Current[0].Count,Is.EqualTo(4));Assert.That(row.Current[1].Count,Is.Zero);
                var appliedMesh=ColliderGeneration.CreateCurrentPreviewMesh(row.Current[0].Shapes,Matrix4x4.identity,false);
                try
                {
                    Assert.That(appliedMesh.vertexCount,Is.EqualTo(originalMesh.vertexCount));
                    var original=originalMesh.vertices;var applied=appliedMesh.vertices;
                    for(int i=0;i<original.Length;i++) Assert.That(Vector3.Distance(original[i],applied[i]),Is.LessThan(.0001f),"world vertex "+i);
                }
                finally { Object.DestroyImmediate(appliedMesh); }
            }
            finally { Object.DestroyImmediate(originalMesh); }
        }
        [Test] public void CurrentEnvelopeRejectsUnrepresentableTypeAndCopiesNativeShapesWithoutQualityGate()
        {
            ConfigureEnvelopeAndPicking();var row=workspace.Items.Single();
            Assert.That(workspace.CanReuseCurrentEnvelope(row,"default"),Is.False);
            AuthorCurrentEnvelope();workspace.ReuseCurrentEnvelope(row,"default");var candidate=row.Candidates[0].Preview;
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var mesh=root.transform.Find("Colliders").gameObject.AddComponent<MeshCollider>();
                mesh.sharedMesh=root.transform.Find("Visual").GetComponent<MeshFilter>().sharedMesh;mesh.convex=true;
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            workspace.RefreshCurrent();Assert.That(workspace.CanReuseCurrentEnvelope(row,"default"),Is.False);
            Assert.Throws<InvalidOperationException>(()=>workspace.ReuseCurrentEnvelope(row,"default"));
            Assert.That(row.Candidates[0].Preview,Is.SameAs(candidate));
            root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                Object.DestroyImmediate(root.transform.Find("Colliders").GetComponent<MeshCollider>());
                root.transform.Find("Colliders/Box").localRotation=Quaternion.Euler(0,23,0);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            workspace.RefreshCurrent();Assert.That(workspace.CanReuseCurrentEnvelope(row,"default"));
            workspace.ReuseCurrentEnvelope(row,"default");
            Assert.That(row.Candidates[0].Preview.ManuallyReused);
            Assert.That(row.CanApply(0));
        }
        [UnityTest] public IEnumerator CurrentEnvelopeButtonCopiesIntoItsOwnCandidate()
        {
            ConfigureEnvelopeAndPicking();AuthorCurrentEnvelope();
            var window=ScriptableObject.CreateInstance<ColliderOverviewWindow>();window.Show();
            try
            {
                window.Workspace.Read(new Object[]{AssetDatabase.LoadAssetAtPath<GameObject>(path)});
                for(int i=0;i<8;i++)yield return null;
                var button=window.rootVisualElement.Q<Button>("collider-reuse-current-0");
                Assert.That(button.text,Is.EqualTo("复用当前"));Assert.That(button.enabledSelf);
                Assert.That(window.rootVisualElement.Q<Button>("collider-reuse-current-1").resolvedStyle.display,Is.EqualTo(DisplayStyle.None));
                var row=window.Workspace.Items.Single();Assert.That(row.Candidates[0].Preview,Is.Null);
                using(var submit=NavigationSubmitEvent.GetPooled()) { submit.target=button;button.SendEvent(submit); }
                Assert.That(row.Candidates[0].Preview.Count,Is.EqualTo(1));Assert.That(row.Candidates[1].Preview,Is.Null);
                Assert.That(row.CanApply(0));
            }
            finally { window.Close(); }
        }
        [UnityTest] public IEnumerator DefaultGenerateApplyAndModesHaveIndependentCaches()
        {
            var row = workspace.Items.Single(); byte[] before = File.ReadAllBytes(path);
            yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
            Assert.That(row.Errors, Is.Empty); var structure = row.ActiveCandidates[0].Preview.ContentHash;
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            workspace.Apply(new[] { row }, ColliderTargetSelection.All);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab.transform.Find("Colliders").gameObject.layer, Is.EqualTo(9));
            Assert.That(prefab.transform.Find("Colliders").GetComponentsInChildren<Collider>().All(c => c.gameObject.layer == 9));
            Assert.That(workspace.Items.Single().IsConfirmed, Is.False);
            workspace.MarkHandled(workspace.Items.Single()); Assert.That(workspace.Items.Single().IsConfirmed);
            workspace.ChangeGoal("default", ColliderGeometryGoal.Envelope);
            row = workspace.Items.Single(); Assert.That(row.ActiveCandidates[0].Preview, Is.Null);
            yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
            Assert.That(row.Errors, Is.Empty); Assert.That(row.ActiveCandidates[0].Preview.Goal, Is.EqualTo(ColliderGeometryGoal.Envelope));
            Assert.That(row.ActiveCandidates[0].Preview.Count, Is.LessThanOrEqualTo(3));
            workspace.ChangeGoal("default", ColliderGeometryGoal.Structure);
            Assert.That(workspace.Items.Single().ActiveCandidates[0].Preview.ContentHash, Is.EqualTo(structure));
            Assert.That(workspace.Items.Single().HasCurrentCandidate("default"));
        }
        [UnityTest] public IEnumerator ConfiguredMeshFolderAndExistingLayerArePreserved()
        {
            var config = workspace.Configuration; config.MeshDirectory = Folder + "/Meshes"; config.Targets[0].Path = "Physics/Shapes";
            workspace.Configure(config); var root = PrefabUtility.LoadPrefabContents(path);
            ColliderAuthoringGroups.Ensure(root.transform, "Physics/Shapes").gameObject.layer = 12;
            PrefabUtility.SaveAsPrefabAsset(root, path); PrefabUtility.UnloadPrefabContents(root); workspace.RefreshCurrent();
            var row = workspace.Items.Single(); yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
            var candidate = row.Candidates[0]; var preview = candidate.Preview;
            preview.Fit.Shapes = new[] { SixPrimitiveMeshes.Convert(new SixPrimitiveMath.Primitive {
                Kind = SixPrimitiveMath.PrimitiveKind.Prism, HalfSize = new Vector3(.3f,.5f,.2f), Bottom = .3f, Top = .2f, Rotation = Quaternion.identity }) };
            workspace.Apply(new[] { row }, ColliderTargetSelection.All);
            Assert.That(row.Errors, Is.Empty);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path); var group = saved.transform.Find("Physics/Shapes");
            Assert.That(group.gameObject.layer, Is.EqualTo(12));
            var mesh = group.GetComponentInChildren<MeshCollider>(); Assert.That(mesh, Is.Not.Null); Assert.That(mesh.convex);
            Assert.That(AssetDatabase.GetAssetPath(mesh.sharedMesh), Does.StartWith(config.MeshDirectory + "/"));
        }
        [UnityTest] public IEnumerator OutsideShapesAndUnselectedTargetSurviveApplyAndReplacement()
        {
            var config = workspace.Configuration;
            config.Targets[0].Path = "Physics/Generated";
            config.Targets = config.Targets.Concat(new[] { new ColliderOutputTarget("second", "第二组", "Picking/Generated", ColliderGeometryGoal.Envelope) }).ToArray();
            workspace.Configure(config);
            var root = PrefabUtility.LoadPrefabContents(path);
            var handmade = new GameObject("Handmade"); handmade.transform.SetParent(root.transform, false); handmade.layer = 17;
            var box = handmade.AddComponent<BoxCollider>(); box.size = new Vector3(2, 3, 4); box.isTrigger = true;
            var backup = handmade.AddComponent<SphereCollider>(); backup.enabled = false; backup.radius = .37f;
            PrefabUtility.SaveAsPrefabAsset(root, path); PrefabUtility.UnloadPrefabContents(root); workspace.RefreshCurrent();
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(saved.transform.Find("Handmade").GetComponent<BoxCollider>(), out string _, out long oldId);
            var row = workspace.Items.Single(); yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
            Assert.That(row.CanApply(0), Is.True, "Unrelated colliders must not block writing a dedicated output group.");
            Assert.That(row.Errors, Is.Empty);
            workspace.Apply(new[] { row }, ColliderTargetSelection.Only("default"));
            saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(saved.transform.Find("Physics/Generated"), Is.Not.Null);
            Assert.That(saved.transform.Find("Picking"), Is.Null);
            workspace.Apply(new[] { row }, ColliderTargetSelection.Only("second"));
            Assert.That(row.Errors, Is.Empty);
            saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var second = saved.transform.Find("Picking/Generated").GetComponentsInChildren<Collider>();
            var secondIds = second.Select(c => { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c, out string _, out long id); return id; }).ToArray();
            workspace.Apply(new[] { row }, ColliderTargetSelection.Only("default"));
            saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var retained = saved.transform.Find("Handmade"); var retainedBox = retained.GetComponent<BoxCollider>();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(retainedBox, out string _, out long currentId);
            Assert.That(currentId, Is.EqualTo(oldId)); Assert.That(retained.gameObject.layer, Is.EqualTo(17));
            Assert.That(retainedBox.size, Is.EqualTo(new Vector3(2, 3, 4))); Assert.That(retainedBox.isTrigger, Is.True);
            Assert.That(retained.GetComponent<SphereCollider>().enabled, Is.False);
            Assert.That(retained.GetComponent<SphereCollider>().radius, Is.EqualTo(.37f));
            CollectionAssert.AreEqual(secondIds, saved.transform.Find("Picking/Generated").GetComponentsInChildren<Collider>()
                .Select(c => { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c, out string _, out long id); return id; }).ToArray());
            Assert.That(row.IsConfirmed, Is.False); workspace.MarkHandled(row);
            Assert.That(row.IsConfirmed, Is.True); Assert.That(row.Errors, Is.Empty);
        }
        [UnityTest] public IEnumerator ExternalEditAndBindingFailureNeverSavePrefab()
        {
            var row = workspace.Items.Single(); yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
            var root = PrefabUtility.LoadPrefabContents(path); root.transform.Find("Visual").localPosition = Vector3.right;
            PrefabUtility.SaveAsPrefabAsset(root, path); PrefabUtility.UnloadPrefabContents(root); var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(() => ColliderWorkspace.ApplyRow(row, ColliderTargetSelection.All));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            using var failing = new ColliderWorkspace(new FailingAdapter()); failing.Read(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) });
            row = failing.Items.Single(); yield return Await(failing.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
            Assert.Throws<InvalidOperationException>(() => ColliderWorkspace.ApplyRow(row, ColliderTargetSelection.All));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        }
        [Test] public void ManualContextUsesConfiguredOutputAndGoal()
        {
            var config = workspace.Configuration; config.Targets[0].Path = "Physics/Shapes"; config.MeshDirectory = Folder + "/Meshes";
            config.Targets[0].ChangeGoal(ColliderGeometryGoal.Envelope); workspace.Configure(config);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var adapter = new DefaultColliderAuthoringAdapter(); adapter.Prepare(root, workspace.Configuration, new[] { "default" });
                var context = ColliderAuthoringContext.Create(root.transform.Find("Physics/Shapes"), ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Envelope));
                Assert.That(context.Request.Layer, Is.EqualTo(9)); Assert.That(context.Request.MeshAssetDirectory, Is.EqualTo(config.MeshDirectory));
                Assert.That(context.Request.Sources.Count, Is.EqualTo(1)); Assert.That(context.Request.AfterApply, Is.Not.Null);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        [Test] public void RemovedTargetsResetRowScopesAndInvalidScopeDoesNotLeaveBusy()
        {
            var config = workspace.Configuration;
            config.Targets = config.Targets.Concat(new[] { new ColliderOutputTarget("other", "第二组", "Other", ColliderGeometryGoal.Envelope) }).ToArray();
            workspace.Configure(config); var row = workspace.Items.Single(); row.GenerateScope = row.ApplyScope = ColliderTargetSelection.Only("other");
            config = workspace.Configuration; config.Targets = config.Targets.Take(1).ToArray(); workspace.Configure(config);
            Assert.That(workspace.Items.Single().GenerateScope, Is.EqualTo(ColliderTargetSelection.All));
            Assert.That(workspace.Items.Single().ApplyScope, Is.EqualTo(ColliderTargetSelection.All));
            var task = workspace.GenerateAsync(workspace.Items.ToArray(), ColliderTargetSelection.Only("missing"));
            Assert.That(task.IsFaulted); Assert.That(task.Exception.GetBaseException(), Is.TypeOf<ArgumentException>()); Assert.That(workspace.Busy, Is.False);
        }
        [UnityTest] public IEnumerator SavedPrefabIsReportedWhenReviewWriteFails()
        {
            string review = "Library/WorkbenchReviews-" + Guid.NewGuid().ToString("N") + ".json";
            using var other = new ColliderWorkspace(reviewPath: review);
            try
            {
                other.Read(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) }); var row = other.Items.Single();
                yield return Await(other.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
                File.Delete(review); Directory.CreateDirectory(review);
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Prefab已保存，但审阅清单保存失败"));
                other.Apply(new[] { row }, ColliderTargetSelection.All);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).transform.Find("Colliders"), Is.Not.Null);
                Assert.That(row.Errors.Any(e => e.Contains("Prefab已保存")));
            }
            finally { if (Directory.Exists(review)) Directory.Delete(review); if (File.Exists(review)) File.Delete(review); }
        }
        sealed class FailingAdapter : IColliderAuthoringAdapter
        {
            readonly DefaultColliderAuthoringAdapter inner = new DefaultColliderAuthoringAdapter();
            public ColliderAuthoringProfile CreateProfile() => inner.CreateProfile();
            public bool CanHandle(GameObject r) => inner.CanHandle(r);
            public ColliderAssetLayout Inspect(GameObject r, ColliderAuthoringProfile p) => inner.Inspect(r,p);
            public void Prepare(GameObject r, ColliderAuthoringProfile p, System.Collections.Generic.IReadOnlyList<string> ids) => inner.Prepare(r,p,ids);
            public ColliderGenerationRequest CreateRequest(GameObject r, ColliderAuthoringProfile p,string id) => inner.CreateRequest(r,p,id);
            public void Complete(GameObject r,System.Collections.Generic.IReadOnlyList<ColliderTargetResult> results) => throw new InvalidOperationException("binding failed");
        }
        [UnityTest] public IEnumerator DefaultWindowUsesThreeColumnsAndVisibleProgress()
        {
            var window = ScriptableObject.CreateInstance<ColliderOverviewWindow>(); window.Show();
            try
            {
                window.Workspace.Read(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) });
                for (int i=0;i<8;i++) yield return null;
                var images = window.rootVisualElement.Query<Image>().ToList(); Assert.That(images.Count,Is.EqualTo(3));
                Assert.That(window.rootVisualElement.Q<ProgressBar>(),Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<Button>("collider-generate-all"),Is.Null);
                Assert.That(window.rootVisualElement.Q<Button>("collider-cancel"),Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<PopupField<string>>("collider-goal-default"),Is.Not.Null);
                Assert.That(window.Scope,Is.EqualTo(ColliderTargetSelection.All));
                Assert.That(window.rootVisualElement.Q<UnityEditor.UIElements.ToolbarMenu>("collider-row-generate").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(window.rootVisualElement.Q<UnityEditor.UIElements.ToolbarMenu>("collider-row-apply").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(window.rootVisualElement.Q<Button>("collider-row-generate-action").text, Is.EqualTo("生成预览"));
                Assert.That(window.rootVisualElement.Q<Button>("collider-row-apply-action").text, Is.EqualTo("应用候选"));
            }
            finally { window.Close(); }
        }
    }
}
