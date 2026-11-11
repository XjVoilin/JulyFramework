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
    internal sealed class TuningTestAdapter : IColliderAuthoringAdapter
    {
        readonly string id, folder;
        readonly bool fixedGoal;
        readonly bool editableTargets;
        readonly DefaultColliderAuthoringAdapter inner = new DefaultColliderAuthoringAdapter();
        internal TuningTestAdapter(string id, string folder, bool fixedGoal = false, bool editableTargets = true) { this.id = id; this.folder = folder; this.fixedGoal = fixedGoal; this.editableTargets = editableTargets; }
        public ColliderAuthoringProfile CreateProfile() => new ColliderAuthoringProfile(id, "逐项测试", new[] {
            new ColliderOutputTarget("whole", "整体包围", "Whole", ColliderGeometryGoal.Envelope, fixedGoal),
            new ColliderOutputTarget("fine", "结构贴合", "Fine", ColliderGeometryGoal.Structure, fixedGoal) }, folder + "/Meshes", editableTargets);
        public bool CanHandle(GameObject root) => inner.CanHandle(root);
        public ColliderAssetLayout Inspect(GameObject root, ColliderAuthoringProfile profile) => inner.Inspect(root, profile);
        public void Prepare(GameObject root, ColliderAuthoringProfile profile, System.Collections.Generic.IReadOnlyList<string> ids) => inner.Prepare(root, profile, ids);
        public ColliderGenerationRequest CreateRequest(GameObject root, ColliderAuthoringProfile profile, string id) => inner.CreateRequest(root, profile, id);
        public void Complete(GameObject root, System.Collections.Generic.IReadOnlyList<ColliderTargetResult> results) { }
    }
    internal sealed class TuningTestWindow : ColliderOverviewWindow
    {
        internal static IColliderAuthoringAdapter Adapter;
        protected override IColliderAuthoringAdapter CreateAdapter() => Adapter;
    }

    public sealed class ColliderItemTuningTests
    {
        string folder, id, first, second;
        byte[] originalSettings;
        ColliderWorkspace workspace;
        TuningTestAdapter adapter;

        [SetUp] public void Setup()
        {
            originalSettings = File.Exists(ColliderAuthoringSettings.FileName) ? File.ReadAllBytes(ColliderAuthoringSettings.FileName) : null;
            id = "item-tuning-test-" + Guid.NewGuid().ToString("N"); folder = "Assets/__" + id;
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            first = Create("a"); second = Create("b"); adapter = new TuningTestAdapter(id, folder);
            workspace = new ColliderWorkspace(adapter); ReadBoth(workspace);
        }
        [TearDown] public void Cleanup()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<ColliderItemSettingsWindow>()) window.Close();
            foreach (var window in Resources.FindObjectsOfTypeAll<TuningTestWindow>()) window.Close();
            workspace.Dispose(); AssetDatabase.DeleteAsset(folder); File.Delete(adapter.CreateProfile().ReviewFile);
            if (originalSettings == null) File.Delete(ColliderAuthoringSettings.FileName);
            else File.WriteAllBytes(ColliderAuthoringSettings.FileName, originalSettings);
        }
        [UnityTest] public IEnumerator LockedOutputProfileStillSavesPerformanceSettingsAndOpensSettingsWindow()
        {
            var lockedAdapter=new TuningTestAdapter(id,folder,true,false);
            using var locked=new ColliderWorkspace(lockedAdapter);
            var draft=locked.Configuration;draft.Performance.TotalVerticesReminder=32;
            draft.Targets[0].Path="MustNotChange";
            locked.Configure(draft);
            using(var reopened=new ColliderWorkspace(lockedAdapter))
            {
                Assert.That(reopened.Configuration.Performance.TotalVerticesReminder,Is.EqualTo(32));
                Assert.That(reopened.Configuration.Targets[0].Path,Is.EqualTo("Whole"));
            }
            ColliderAuthoringSettingsWindow.Open(locked,()=>{});
            var window=Resources.FindObjectsOfTypeAll<ColliderAuthoringSettingsWindow>().Single();
            try { window.position=new Rect(0,0,480,600);for(int i=0;i<5;i++)yield return null; }
            finally { window.Close(); }
        }
        [UnityTest] public IEnumerator ProgressTicksDoNotRefreshTheWholeLoadedQueue()
        {
            int refreshes=0;workspace.Changed+=()=>refreshes++;
            var row=workspace.Items[0];
            yield return Await(workspace.GenerateAsync(new[]{row},ColliderTargetSelection.Only("whole")));
            Assert.That(row.Errors,Is.Empty);
            Assert.That(row.ActiveCandidates[0].Progress,Is.EqualTo(ColliderProgress.Complete));
            Assert.That(refreshes,Is.LessThanOrEqualTo(8),"Work-bar ticks must not trigger whole-queue statistics and view refreshes.");
        }
        string Create(string name)
        {
            var root = new GameObject(name); var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(visual.GetComponent<Collider>()); visual.transform.SetParent(root.transform, false);
            var path = folder + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root); return path;
        }
        void ReadBoth(ColliderWorkspace value) => value.Read(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(first), AssetDatabase.LoadAssetAtPath<GameObject>(second) });
        static IEnumerator Await(Task task) { while (!task.IsCompleted) yield return null; if (task.IsFaulted) throw task.Exception; }
        static void Tune(ColliderWorkspace value, ColliderRow row, string target, int count, float error = 4)
        { value.SetGenerationSettings(row, target, count, error); }
        static void Click(Button button)
        { using var e = NavigationSubmitEvent.GetPooled(); e.target = button; button.SendEvent(e); }

        [Test] public void ItemColumnAndGoalOverridesPersistAndInheritOtherDefaults()
        {
            var row = workspace.Items[0]; Tune(workspace, row, "fine", 6);
            Assert.That(row.GenerationSettings("fine").TargetCount, Is.EqualTo(6));
            Assert.That(row.GenerationSettings("whole").TargetCount, Is.EqualTo(3));
            Assert.That(workspace.Items[1].GenerationSettings("fine").TargetCount, Is.EqualTo(8));
            var copy = row.GenerationSettings("fine"); copy.TargetCount = 25;
            Assert.That(row.GenerationSettings("fine").TargetCount, Is.EqualTo(6));
            var config = workspace.Configuration; config.Targets[1].Settings.MinimumThicknessPercent = .7f;
            workspace.Configure(config); row = workspace.Items[0];
            Assert.That(row.GenerationSettings("fine").MinimumThicknessPercent, Is.EqualTo(.7f));
            workspace.ChangeGoal("fine", ColliderGeometryGoal.Envelope); row = workspace.Items[0];
            Assert.That(row.GenerationSettings("fine").TargetCount, Is.EqualTo(3)); Tune(workspace, row, "fine", 5);
            workspace.ChangeGoal("fine", ColliderGeometryGoal.Structure); row = workspace.Items[0];
            Assert.That(row.GenerationSettings("fine").TargetCount, Is.EqualTo(6));
            using var reopened = new ColliderWorkspace(adapter); ReadBoth(reopened);
            Assert.That(reopened.Items[0].GenerationSettings("fine").TargetCount, Is.EqualTo(6));
            reopened.ResetGenerationSettings(reopened.Items[0], "fine");
            Assert.That(reopened.Items[0].GenerationSettings("fine").TargetCount, Is.EqualTo(8));
        }

        [UnityTest] public IEnumerator OldCandidatesRemainVisibleButCannotApplyWithDifferentParameters()
        {
            var row = workspace.Items[0]; yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
            var previews = row.ActiveCandidates.Select(c => c.Preview).ToArray();
            var cachePath = ColliderCache.PathFor(row.AssetPath, workspace.Profile); var cached = File.ReadAllBytes(cachePath);
            var asset = File.ReadAllBytes(first); Tune(workspace, row, "whole", 6);
            Assert.That(row.HasCurrentCandidate("whole"), Is.False); Assert.That(row.HasCurrentCandidate("fine"), Is.True);
            Assert.That(row.ActiveCandidates[0].Preview, Is.SameAs(previews[0]));
            CollectionAssert.AreEqual(cached, File.ReadAllBytes(cachePath));
            Assert.Throws<InvalidOperationException>(() => ColliderWorkspace.ApplyRow(row, ColliderTargetSelection.Only("whole")));
            CollectionAssert.AreEqual(asset, File.ReadAllBytes(first));
            workspace.ResetGenerationSettings(row, "whole"); Assert.That(row.HasCurrentCandidate("whole"));
            Tune(workspace, row, "whole", 6);
            yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.Only("whole")));
            Assert.That(row.HasCurrentCandidate("whole"));
            Assert.That(row.ActiveCandidates[0].Preview.MatchesSettings(row.GenerationSettings("whole")));
            Assert.That(row.ActiveCandidates[1].Preview, Is.SameAs(previews[1]));
            ReadBoth(workspace); row = workspace.Items[0];
            Assert.That(row.HasCurrentCandidate("whole")); Assert.That(row.GenerationSettings("whole").TargetCount, Is.EqualTo(6));
        }

        [UnityTest] public IEnumerator ExplicitRowsUseEachItemsParametersAndRecomputeSuccessfulCandidates()
        {
            Tune(workspace, workspace.Items[0], "fine", 6); Tune(workspace, workspace.Items[1], "whole", 5);
            yield return Await(workspace.GenerateAsync(workspace.Items.ToArray(), ColliderTargetSelection.All));
            var old = workspace.Items.SelectMany(r => r.ActiveCandidates).Select(c => c.Preview).ToArray();
            Assert.That(workspace.Items.SelectMany(r => Enumerable.Range(0, 2).Select(i => r.ActiveCandidates[i].Preview.MatchesSettings(r.EffectiveSettings(i)))).All(x => x));
            yield return Await(workspace.GenerateAsync(workspace.Items.ToArray(), ColliderTargetSelection.All));
            Assert.That(workspace.Items.SelectMany(r => r.ActiveCandidates).Select((c, i) => !ReferenceEquals(c.Preview, old[i])).All(x => x));
            foreach (var row in workspace.Items) Assert.That(row.IsConfirmed, Is.False);
        }

        [UnityTest] public IEnumerator CompletionAndReturnOnlyChangeReviewAndActualChangesRequireRecheck()
        {
            var row = workspace.Items[0]; yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
            ColliderWorkspace.ApplyRow(row, ColliderTargetSelection.All);
            Assert.That(row.IsConfirmed, Is.False); Assert.That(row.InQueue(ColliderQueue.Pending));
            var asset = File.ReadAllBytes(first); var cache = File.ReadAllBytes(ColliderCache.PathFor(first, workspace.Profile));
            workspace.MarkHandled(row); Assert.That(row.InQueue(ColliderQueue.Done)); Assert.That(row.InQueue(ColliderQueue.Pending), Is.False);
            Assert.That(row.IsNew, Is.False); workspace.MarkPending(row);
            Assert.That(row.InQueue(ColliderQueue.Pending)); Assert.That(row.InQueue(ColliderQueue.Done), Is.False); Assert.That(row.IsNew, Is.False);
            CollectionAssert.AreEqual(asset, File.ReadAllBytes(first)); CollectionAssert.AreEqual(cache, File.ReadAllBytes(ColliderCache.PathFor(first, workspace.Profile)));
            workspace.MarkHandled(row); Tune(workspace, row, "fine", 6); Assert.That(row.IsConfirmed);
            yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.Only("fine")));
            Assert.That(row.IsConfirmed, Is.True, "a candidate does not replace the confirmed current result");
            row.ActiveCandidates[1].Preview.Fit.Shapes[0].Center += Vector3.right * .1f;
            ColliderWorkspace.ApplyRow(row, ColliderTargetSelection.Only("fine"));
            Assert.That(row.InQueue(ColliderQueue.Recheck)); Assert.That(row.IsConfirmed, Is.False);
            workspace.MarkHandled(row); Assert.That(row.IsConfirmed);
            ReadBoth(workspace); Assert.That(workspace.Items[0].IsConfirmed);
        }

        [Test] public void SaveFailureRollsBackOverrideAndStaleRowCannotWrite()
        {
            var row = workspace.Items[0]; var path = workspace.Profile.ReviewFile; var backup = path + ".test-backup";
            File.Move(path, backup); Directory.CreateDirectory(path);
            try
            {
                Assert.Throws<IOException>(() => Tune(workspace, row, "fine", 6));
                Assert.That(row.GenerationSettings("fine").TargetCount, Is.EqualTo(8));
            }
            finally { Directory.Delete(path); File.Move(backup, path); }
            ReadBoth(workspace); Assert.Throws<InvalidOperationException>(() => Tune(workspace, row, "fine", 6));
            Assert.Throws<ArgumentException>(() => workspace.SetGenerationSettings(workspace.Items[0], "fine", 129, 4));
            Assert.Throws<ArgumentException>(() => workspace.SetGenerationSettings(workspace.Items[0], "fine", 17, 4));
        }

        [Test] public void MeshBudgetsPersistPerItemAndLegacyOverridesInheritDefaults()
        {
            var row = workspace.Items[0]; Tune(workspace, row, "fine", 6);
            var config = workspace.Configuration;
            config.Targets[1].Settings.MaxHullVertices = 20;
            config.Targets[1].Settings.PickingInwardErrorPercent = 4;
            workspace.Configure(config); row = workspace.Items[0];
            Assert.That(row.GenerationSettings("fine").MaxHullVertices, Is.EqualTo(20));
            workspace.SetGenerationSettings(row, "fine", 6, 4, 24, 2);
            using (var reopened = new ColliderWorkspace(adapter))
            {
                ReadBoth(reopened);
                Assert.That(reopened.Items[0].GenerationSettings("fine").MaxHullVertices, Is.EqualTo(24));
                Assert.That(reopened.Items[0].GenerationSettings("fine").PickingInwardErrorPercent, Is.EqualTo(2));
                Assert.That(reopened.Items[1].GenerationSettings("fine").MaxHullVertices, Is.EqualTo(20));
            }
            workspace.ChangeGoal("fine", ColliderGeometryGoal.Envelope);
            Assert.That(workspace.Items[0].Recipe(1), Is.Null);
            workspace.ChangeGoal("fine", ColliderGeometryGoal.Structure); row = workspace.Items[0];
            Assert.That(row.GenerationSettings("fine").MaxHullVertices, Is.EqualTo(24));
            workspace.ResetGenerationSettings(row, "fine");
            Assert.That(row.GenerationSettings("fine").MaxHullVertices, Is.EqualTo(20));
            Assert.Throws<ArgumentException>(() => workspace.SetGenerationSettings(row, "fine", 8, 4, 3, 2));
        }

        [Test] public void PickingShapeModePersistsPerItemAndCanRestoreInheritance()
        {
            var row = workspace.Items[0];
            Assert.That(row.GenerationSettings("fine").PickingShapes,Is.EqualTo(PickingShapeMode.PreferPrimitives));
            workspace.SetGenerationSettings(row,"fine",8,8,pickingShapes:PickingShapeMode.ConvexOnly);
            using (var reopened = new ColliderWorkspace(adapter))
            {
                ReadBoth(reopened);
                Assert.That(reopened.Items[0].GenerationSettings("fine").PickingShapes,Is.EqualTo(PickingShapeMode.ConvexOnly));
                Assert.That(reopened.Items[1].GenerationSettings("fine").PickingShapes,Is.EqualTo(PickingShapeMode.PreferPrimitives));
            }
            workspace.ResetGenerationSettings(row,"fine");
            Assert.That(row.GenerationSettings("fine").PickingShapes,Is.EqualTo(PickingShapeMode.PreferPrimitives));
            var legacy = JsonUtility.FromJson<ColliderGenerationSettings>("{\"Goal\":1,\"TargetCount\":8}");
            Assert.That(legacy.PickingShapes,Is.EqualTo(PickingShapeMode.PreferPrimitives));
            Assert.That(legacy.NativeShapeErrorPercent,Is.EqualTo(10));
            Assert.That(legacy.NativeClickChangePercent,Is.EqualTo(20));
        }

        [Test] public void NativeTolerancesPersistPerItemAndRestoreProfileDefaults()
        {
            var row=workspace.Items[0];
            workspace.SetGenerationSettings(row,"fine",8,8,nativeShapeErrorPercent:8,nativeClickChangePercent:20);
            using(var reopened=new ColliderWorkspace(adapter))
            {
                ReadBoth(reopened);
                Assert.That(reopened.Items[0].GenerationSettings("fine").NativeShapeErrorPercent,Is.EqualTo(8));
                Assert.That(reopened.Items[0].GenerationSettings("fine").NativeClickChangePercent,Is.EqualTo(20));
                Assert.That(reopened.Items[1].GenerationSettings("fine").NativeShapeErrorPercent,Is.EqualTo(10));
                Assert.That(reopened.Items[1].GenerationSettings("fine").NativeClickChangePercent,Is.EqualTo(20));
            }
            workspace.ResetGenerationSettings(row,"fine");
            Assert.That(row.GenerationSettings("fine").NativeShapeErrorPercent,Is.EqualTo(10));
            Assert.Throws<ArgumentException>(()=>workspace.SetGenerationSettings(row,"fine",8,8,nativeShapeErrorPercent:float.NaN));
            Assert.Throws<ArgumentException>(()=>workspace.SetGenerationSettings(row,"fine",8,8,nativeClickChangePercent:31));
        }

        [UnityTest] public IEnumerator FailedVertexBudgetKeepsLastSuccessfulCandidateAndCache()
        {
            var row = workspace.Items[0];
            yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.Only("fine")));
            var old = row.ActiveCandidates[1].Preview;
            Assert.That(old, Is.Not.Null);
            var cache = File.ReadAllBytes(ColliderCache.PathFor(first, workspace.Profile));
            var asset = File.ReadAllBytes(first);
            workspace.SetGenerationSettings(row, "fine", 8, 4, 4, 0, PickingShapeMode.ConvexOnly);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("InvalidOperationException: 顶点预算"));
            yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.Only("fine")));
            Assert.That(row.ActiveCandidates[1].Progress, Is.EqualTo(ColliderProgress.Failed));
            Assert.That(row.ActiveCandidates[1].Preview, Is.SameAs(old));
            CollectionAssert.AreEqual(cache, File.ReadAllBytes(ColliderCache.PathFor(first, workspace.Profile)));
            CollectionAssert.AreEqual(asset, File.ReadAllBytes(first));
            Assert.That(row.HasCurrentCandidate("fine"), Is.False);
        }

        [UnityTest] public IEnumerator WarmGenerationReusesStagesAndForcedAnalysisKeepsSameOutput()
        {
            var row = workspace.Items[0]; var scope = ColliderTargetSelection.Only("fine");
            var prefab = File.ReadAllBytes(first);
            yield return Await(workspace.GenerateAsync(new[]{row},scope));
            yield return Await(workspace.GenerateAsync(new[]{row},scope));
            var warm = row.ActiveCandidates[1].Preview;
            Assert.That(warm.Summary, Does.Contain("复用拟合"));
            yield return Await(workspace.GenerateAsync(new[]{row},scope,true));
            var forced = row.ActiveCandidates[1].Preview;
            Assert.That(forced.Summary, Does.Not.Contain("复用"));
            Assert.That(forced.ContentHash, Is.EqualTo(warm.ContentHash));
            Assert.That(forced, Is.Not.SameAs(warm));
            CollectionAssert.AreEqual(prefab,File.ReadAllBytes(first));
        }

        [Test] public void InvalidStoredOverridesFailWithoutResettingAuthorData()
        {
            Tune(workspace, workspace.Items[0], "fine", 6);
            var path = workspace.Profile.ReviewFile;
            string valid = File.ReadAllText(path);
            Assert.That(valid, Does.Contain("\"targetCount\": 6"));
            string invalid = valid.Replace("\"targetCount\": 6", "\"targetCount\": 0");
            File.WriteAllText(path, invalid);
            Assert.Throws<InvalidDataException>(() => new ColliderWorkspace(adapter));
            Assert.That(File.ReadAllText(path), Is.EqualTo(invalid));
        }

        [Test] public void LegacyLargerCountLoadsAtSixteenWithoutRewritingReview()
        {
            Tune(workspace, workspace.Items[0], "fine", 6);
            var path = workspace.Profile.ReviewFile;
            string legacy = File.ReadAllText(path).Replace("\"targetCount\": 6", "\"targetCount\": 32");
            File.WriteAllText(path, legacy);
            using var reopened = new ColliderWorkspace(adapter); ReadBoth(reopened);
            Assert.That(reopened.Items[0].GenerationSettings("fine").TargetCount, Is.EqualTo(16));
            Assert.That(File.ReadAllText(path), Is.EqualTo(legacy));
            Assert.Throws<ArgumentException>(() => reopened.SetGenerationSettings(reopened.Items[0], "fine", 17, 4));
        }

        [UnityTest] public IEnumerator BusyAndCancelledGenerationProtectOverridesAndLastSuccessfulCandidate()
        {
            var row = workspace.Items[0];
            yield return Await(workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
            var old = row.ActiveCandidates[1].Preview;
            Tune(workspace, row, "fine", 6);
            var task = workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.Only("fine"));
            Assert.Throws<InvalidOperationException>(() => Tune(workspace, row, "fine", 7));
            Assert.Throws<InvalidOperationException>(() => workspace.MarkHandled(row));
            workspace.Cancel(); yield return Await(task);
            Assert.That(row.ActiveCandidates[1].Preview, Is.SameAs(old));
            Assert.That(row.GenerationSettings("fine").TargetCount, Is.EqualTo(6));
            workspace.ResetGenerationSettings(row, "fine"); Assert.That(row.HasCurrentCandidate("fine"));
        }

        [UnityTest] public IEnumerator FooterKeepsSelectionScopeAndActionsTogetherAndPreservesErrors()
        {
            TuningTestWindow.Adapter = new TuningTestAdapter(id, folder, true);
            var window = ScriptableObject.CreateInstance<TuningTestWindow>(); window.position = new Rect(0, 0, 1100, 700); window.Show();
            try
            {
                ReadBoth(window.Workspace); for (int i = 0; i < 8; i++) yield return null;
                var root = window.rootVisualElement; var actions = root.Q("collider-selection-actions");
                var generate = root.Q<Button>("collider-generate-selected"); var apply = root.Q<Button>("collider-apply-selected");
                var scope = root.Q<PopupField<string>>("collider-scope");
                Assert.That(scope.value, Is.EqualTo("整体包围＋结构贴合")); Assert.That(window.Scope, Is.EqualTo(ColliderTargetSelection.All));
                Assert.That(generate.parent, Is.SameAs(actions)); Assert.That(apply.parent, Is.SameAs(actions)); Assert.That(scope.parent, Is.SameAs(actions));
                Assert.That(root.Q<Label>("collider-selection-count").text, Is.EqualTo("已选 0"));
                Assert.That(root.Q("collider-bulk").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                foreach (int width in new[] { 1100, 740 })
                {
                    window.position = new Rect(0, 0, width, 700); for (int i = 0; i < 8; i++) yield return null;
                    Assert.That(actions.resolvedStyle.height, Is.LessThanOrEqualTo(32));
                    Assert.That(scope.worldBound.xMax, Is.LessThanOrEqualTo(generate.worldBound.xMin));
                    Assert.That(generate.worldBound.xMax, Is.LessThanOrEqualTo(apply.worldBound.xMin));
                    Assert.That(apply.worldBound.xMax, Is.LessThanOrEqualTo(root.worldBound.xMax));
                    Assert.That(generate.resolvedStyle.width, Is.EqualTo(apply.resolvedStyle.width).Within(.1));
                    window.Workspace.Items[0].Checked = true; window.Refresh(); for (int i = 0; i < 5; i++) yield return null;
                    Assert.That(root.Q("collider-bulk").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                    Assert.That(root.Q<Label>("collider-write-summary").text, Does.Contain("无预览 2组"));
                    Assert.That(root.Q<Label>("collider-selection-count").text, Is.EqualTo("已选 1"));
                    window.Workspace.Items[0].Checked = false; window.Refresh(); for (int i = 0; i < 5; i++) yield return null;
                }
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("fixture footer error"));
                window.Run(() => throw new InvalidOperationException("fixture footer error"));
                for (int i = 0; i < 5; i++) yield return null;
                Assert.That(root.Q<Label>("collider-message").text, Does.Contain("fixture footer error"));
                Assert.That(root.Q<Label>("collider-message").parent.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                scope.index = 1; Assert.That(window.Scope, Is.EqualTo(ColliderTargetSelection.Only("whole")));
                Assert.That(window.Workspace.Items.All(r => r.ActiveCandidates.All(c => c.Preview == null)), "scope choice still does not generate");
            }
            finally { window.Close(); }
        }

        [UnityTest] public IEnumerator SelectedButtonsOnlyGenerateAndApplyVisibleCheckedRows()
        {
            TuningTestWindow.Adapter = new TuningTestAdapter(id, folder, true);
            var window = ScriptableObject.CreateInstance<TuningTestWindow>(); window.position = new Rect(0, 0, 900, 650); window.Show();
            try
            {
                ReadBoth(window.Workspace); for (int i = 0; i < 8; i++) yield return null;
                var root = window.rootVisualElement; var rows = window.Workspace.Items;
                var firstRow = rows.Single(r => r.Path == first); var secondRow = rows.Single(r => r.Path == second);
                var gen = root.Q<Button>("collider-generate-selected"); var apply = root.Q<Button>("collider-apply-selected");
                Assert.That(root.Q<Button>("collider-generate-all"), Is.Null);
                Assert.That(gen.text, Is.EqualTo("生成选中预览")); Assert.That(apply.text, Is.EqualTo("应用选中预览"));
                Assert.That(gen.parent.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(gen.resolvedStyle.height, Is.GreaterThan(20));
                Assert.That(gen.enabledSelf || apply.enabledSelf, Is.False);
                byte[] firstBytes = File.ReadAllBytes(first), secondBytes = File.ReadAllBytes(second);
                string secondReview = JsonUtility.ToJson(secondRow.Review);
                string secondCache = ColliderCache.PathFor(second, window.Workspace.Profile); bool cacheExisted = File.Exists(secondCache);
                byte[] cacheBytes = cacheExisted ? File.ReadAllBytes(secondCache) : null;
                firstRow.Checked = secondRow.Checked = true;
                root.Q<UnityEditor.UIElements.ToolbarSearchField>("collider-search").value = "/a.prefab";
                window.Refresh(); yield return null;
                Assert.That(window.SelectedRows().Length, Is.EqualTo(1)); Assert.That(gen.enabledSelf);
                Click(gen); while (window.Workspace.Busy) yield return null;
                Assert.That(firstRow.ActiveCandidates.All(c => c.Preview != null));
                Assert.That(secondRow.ActiveCandidates.All(c => c.Preview == null), "hidden checked item must not be generated");
                CollectionAssert.AreEqual(firstBytes, File.ReadAllBytes(first)); CollectionAssert.AreEqual(secondBytes, File.ReadAllBytes(second));
                Assert.That(apply.enabledSelf); Click(apply); yield return null;
                Assert.That(firstRow.Current.All(c => c.Count > 0)); Assert.That(secondRow.Current.All(c => c.Count == 0));
                CollectionAssert.AreEqual(secondBytes, File.ReadAllBytes(second));
                Assert.That(JsonUtility.ToJson(secondRow.Review), Is.EqualTo(secondReview));
                Assert.That(File.Exists(secondCache), Is.EqualTo(cacheExisted));
                if (cacheExisted) CollectionAssert.AreEqual(cacheBytes, File.ReadAllBytes(secondCache));
                firstRow.Checked = false; window.Refresh(); yield return null;
                Assert.That(secondRow.Checked, Is.True);
                Assert.That(gen.enabledSelf || apply.enabledSelf, Is.False, "no visible selection cannot operate on hidden selections");
            }
            finally { window.Close(); }
        }

        [UnityTest] public IEnumerator RowActionsChooseScopesBeforeExecutionAndKeepIndependentDefaults()
        {
            TuningTestWindow.Adapter = new TuningTestAdapter(id, folder, true);
            var window = ScriptableObject.CreateInstance<TuningTestWindow>(); window.position = new Rect(0, 0, 900, 650); window.Show();
            try
            {
                window.Workspace.Read(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(first) });
                for (int i = 0; i < 8; i++) yield return null;
                var row = window.Workspace.Items.Single(); var root = window.rootVisualElement;
                var gen = root.Q<UnityEditor.UIElements.ToolbarMenu>("collider-row-generate");
                var app = root.Q<UnityEditor.UIElements.ToolbarMenu>("collider-row-apply");
                var generate = root.Q<Button>("collider-row-generate-action"); var apply = root.Q<Button>("collider-row-apply-action");
                Assert.That(row.GenerateScope, Is.EqualTo(ColliderTargetSelection.All)); Assert.That(row.ApplyScope, Is.EqualTo(ColliderTargetSelection.All));
                Assert.That(gen.text, Is.EqualTo("整体包围＋结构贴合")); Assert.That(app.text, Is.EqualTo("整体包围＋结构贴合"));
                Assert.That(generate.text, Is.EqualTo("生成预览")); Assert.That(apply.text, Is.EqualTo("应用候选"));
                CollectionAssert.AreEqual(new[] { "整体包围＋结构贴合", "整体包围", "结构贴合" }, gen.menu.MenuItems().OfType<DropdownMenuAction>().Select(a => a.name).ToArray());
                byte[] original = File.ReadAllBytes(first);
                ((DropdownMenuAction)gen.menu.MenuItems()[1]).Execute(); ((DropdownMenuAction)app.menu.MenuItems()[2]).Execute();
                Assert.That(row.GenerateScope, Is.EqualTo(ColliderTargetSelection.Only("whole"))); Assert.That(row.ApplyScope, Is.EqualTo(ColliderTargetSelection.Only("fine")));
                Assert.That(gen.text, Is.EqualTo("整体包围")); Assert.That(app.text, Is.EqualTo("结构贴合"));
                Assert.That(row.ActiveCandidates.All(c => c.Preview == null)); CollectionAssert.AreEqual(original, File.ReadAllBytes(first));
                Click(generate); while (window.Workspace.Busy) yield return null;
                Assert.That(row.HasCurrentCandidate("whole")); Assert.That(row.ActiveCandidates[1].Preview, Is.Null);
                Assert.That(apply.enabledSelf, Is.False, "apply scope stays independent from generation scope");
                ((DropdownMenuAction)app.menu.MenuItems()[1]).Execute();
                Assert.That(apply.enabledSelf); CollectionAssert.AreEqual(original, File.ReadAllBytes(first));
                Click(apply); yield return null;
                Assert.That(row.Current[0].Count, Is.GreaterThan(0)); Assert.That(row.Current[1].Count, Is.Zero);
                Assert.That(generate.text, Is.EqualTo("生成预览")); Assert.That(apply.text, Is.EqualTo("应用候选"));
                root.Q<Label>(className: "collider-name").text = "g110_very_long_prefab_name_for_collider_review";
                foreach (int width in new[] { 900, 740 })
                {
                    window.position = new Rect(0, 0, width, 650); for (int i = 0; i < 8; i++) yield return null;
                    Assert.That(gen.worldBound.xMax, Is.LessThanOrEqualTo(generate.worldBound.xMin + .1f));
                    Assert.That(app.worldBound.xMax, Is.LessThanOrEqualTo(apply.worldBound.xMin + .1f));
                    Assert.That(gen.resolvedStyle.width, Is.EqualTo(app.resolvedStyle.width).Within(.1));
                    Assert.That(generate.resolvedStyle.width, Is.EqualTo(apply.resolvedStyle.width).Within(.1));
                    Assert.That(root.Q<Label>(className: "collider-name").resolvedStyle.width, Is.GreaterThan(100));
                    Assert.That(root.Q<Button>("collider-row-review-action").worldBound.xMax, Is.LessThanOrEqualTo(root.worldBound.xMax));
                }
            }
            finally { window.Close(); }
        }

        [UnityTest] public IEnumerator StatusAndAttentionFiltersHaveIndependentScopedCounts()
        {
            TuningTestWindow.Adapter = new TuningTestAdapter(id, folder, true);
            var window = ScriptableObject.CreateInstance<TuningTestWindow>(); window.position = new Rect(0, 0, 900, 650); window.Show();
            try
            {
                ReadBoth(window.Workspace); var done = window.Workspace.Items.Single(r => r.Path == first);
                yield return Await(window.Workspace.GenerateAsync(new[] { done }, ColliderTargetSelection.All));
                window.Workspace.Apply(new[] { done }, ColliderTargetSelection.All); window.Workspace.MarkHandled(done);
                done.ActiveCandidates[0].Error = "fixture failed subsequent candidate"; window.Refresh();
                for (int i = 0; i < 8; i++) yield return null;
                var root = window.rootVisualElement; var list = root.Q<ListView>();
                Assert.That(root.Q<Button>("collider-tag-All").text, Is.EqualTo("全部 2"));
                Assert.That(root.Q<Button>("collider-tag-Pending").text, Is.EqualTo("待处理 1"));
                Assert.That(root.Q<Button>("collider-tag-Done").text, Is.EqualTo("已完成 1"));
                Assert.That(root.Q<Button>("collider-filter-All").text, Is.EqualTo("不限 1"));
                Assert.That(root.Q<Button>("collider-filter-Issues").text, Is.EqualTo("异常 1"));
                Assert.That(root.Q<Button>("collider-filter-New").text, Is.EqualTo("新纳入 1"));
                Click(root.Q<Button>("collider-filter-Issues")); yield return null;
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                Click(root.Q<Button>("collider-filter-New")); yield return null;
                Click(root.Q<Button>("collider-tag-Done")); for (int i = 0; i < 5; i++) yield return null;
                Assert.That(list.itemsSource.Count, Is.EqualTo(1), "completed items ignore the retained new-item filter");
                Assert.That(root.Q("collider-detail-filters").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(root.Q<Button>("collider-clear-filters").parent, Is.SameAs(root.Q("collider-status-tabs")));
                Click(root.Q<Button>("collider-tag-All")); for (int i = 0; i < 5; i++) yield return null;
                Assert.That(root.Q("collider-detail-filters").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(list.itemsSource.Count, Is.EqualTo(1), "secondary filter survives parent switch");
                Assert.That(root.Q<Button>("collider-filter-Issues").text, Is.EqualTo("异常 2"));
                Click(root.Q<Button>("collider-filter-Recheck")); for (int i = 0; i < 5; i++) yield return null;
                Assert.That(list.itemsSource.Count, Is.Zero);
                Assert.That(root.Q("collider-columns").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Click(root.Q<Button>("collider-filter-All")); yield return null;
                Assert.That(list.itemsSource.Count, Is.EqualTo(2));
                var search = root.Q<UnityEditor.UIElements.ToolbarSearchField>("collider-search"); search.value = "no-matching-model";
                Assert.That(list.itemsSource.Count, Is.Zero);
                Assert.That(root.Q<Button>("collider-filter-All").text, Is.EqualTo("不限 2"), "counts describe parent scope, independent of search");
                Click(root.Q<Button>("collider-clear-filters")); yield return null;
                Assert.That(list.itemsSource.Count, Is.EqualTo(2)); Assert.That(search.value, Is.Empty);
                Assert.That(root.Q<Button>("collider-tag-All").ClassListContains("collider-tag-active"));
                Assert.That(root.Q<Button>("collider-filter-All").ClassListContains("collider-tag-active"));
                done.ActiveCandidates[0].Error = null; window.Workspace.MarkPending(done); yield return null;
                Assert.That(root.Q<Button>("collider-tag-Pending").text, Is.EqualTo("待处理 2"));
                Assert.That(root.Q<Button>("collider-tag-Done").text, Is.EqualTo("已完成 0"));
                Assert.That(root.Q<Button>("collider-filter-Issues").text, Is.EqualTo("异常 1"));
                foreach (int width in new[] { 900, 740 })
                {
                    window.position = new Rect(0, 0, width, 650); for (int i = 0; i < 8; i++) yield return null;
                    Assert.That(root.Q("collider-detail-filters").worldBound.yMin, Is.GreaterThan(root.Q("collider-status-tabs").worldBound.yMin));
                    Assert.That(root.Q<Button>("collider-filter-Recheck").worldBound.xMax, Is.LessThanOrEqualTo(root.worldBound.xMax));
                    Assert.That(root.Q<Button>("collider-select-none").worldBound.xMin, Is.GreaterThanOrEqualTo(root.Q<Button>("collider-select-all").worldBound.xMax));
                    Assert.That(root.Q<Button>("collider-select-none").resolvedStyle.width, Is.GreaterThan(40));
                }
                byte[] firstBytes = File.ReadAllBytes(first), secondBytes = File.ReadAllBytes(second), reviewBytes = File.ReadAllBytes(window.Workspace.Profile.ReviewFile);
                Click(root.Q<Button>("collider-select-all")); yield return null;
                Assert.That(window.Workspace.Items.All(r => r.Checked));
                search.value = "/a.prefab"; yield return null;
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                Click(root.Q<Button>("collider-select-none")); yield return null;
                Assert.That(window.Workspace.Items.Single(r => r.Path == first).Checked, Is.False);
                Assert.That(window.Workspace.Items.Single(r => r.Path == second).Checked, Is.True, "hidden selections remain untouched");
                search.value = ""; yield return null;
                Click(root.Q<Button>("collider-select-none")); yield return null;
                Assert.That(window.Workspace.Items.All(r => !r.Checked));
                CollectionAssert.AreEqual(firstBytes, File.ReadAllBytes(first));
                CollectionAssert.AreEqual(secondBytes, File.ReadAllBytes(second));
                CollectionAssert.AreEqual(reviewBytes, File.ReadAllBytes(window.Workspace.Profile.ReviewFile));
            }
            finally { window.Close(); }
        }

        [UnityTest] public IEnumerator PerformanceScopesCombineColumnsDeduplicateItemsAndRespectSearchWithoutWritingAssets()
        {
            TuningTestWindow.Adapter=new TuningTestAdapter(id,folder,true);
            var window=ScriptableObject.CreateInstance<TuningTestWindow>();window.position=new Rect(0,0,1000,720);window.Show();
            try
            {
                ReadBoth(window.Workspace);var rows=window.Workspace.Items.ToArray();
                yield return Await(window.Workspace.GenerateAsync(rows,ColliderTargetSelection.All));
                var mesh=ColliderPreviewDisplay.BoxMesh(Vector3.one);
                var hull=PickingProxy.ConvexHull.Build(mesh.vertices.Select(p=>new PickingProxy.Vec3(p.x,p.y,p.z)).ToArray()).Mesh;
                Object.DestroyImmediate(mesh);
                var shape=new PrimitiveColliderFitter.Shape{Type=PrimitiveColliderFitter.Kind.Convex,
                    ConvexVertices=hull.Vertices.Select(p=>new Vector3((float)p.X,(float)p.Y,(float)p.Z)).ToArray(),ConvexTriangles=hull.Indices};
                rows[0].ActiveCandidates[1].Preview.Fit.Shapes=Enumerable.Repeat(shape,8).ToArray();
                rows[1].ActiveCandidates[1].Preview.Fit.Shapes=new[]{shape};
                var native=rows[1].ActiveCandidates[0].Preview.Fit.Shapes[0];
                rows[1].ActiveCandidates[0].Preview.Fit.Shapes=Enumerable.Repeat(native,7).ToArray();
                window.Refresh();for(int i=0;i<8;i++)yield return null;
                byte[] firstBytes=File.ReadAllBytes(first),reviewBytes=File.ReadAllBytes(window.Workspace.Profile.ReviewFile);
                var root=window.rootVisualElement;var list=root.Q<ListView>();
                var sources=root.Query<Toggle>().ToList().Where(t=>t.name.StartsWith("collider-performance-source-")).ToArray();
                Assert.That(sources.Length,Is.EqualTo(4));Assert.That(sources.All(t=>t.value));
                Assert.That(root.Q<PopupField<string>>("collider-performance-source"),Is.Null);
                Assert.That(root.Q<PopupField<string>>("collider-performance-filter"),Is.Null);
                Assert.That(root.Q<Label>("collider-performance-summary").text,Does.Contain("重点 2"));
                Click(root.Q<Button>("collider-performance-filter-Attention"));
                Assert.That(list.itemsSource.Count,Is.EqualTo(2),"Different chosen columns can independently match.");
                var whole=root.Q<Toggle>("collider-performance-source-whole-1");
                var fine=root.Q<Toggle>("collider-performance-source-fine-1");
                whole.value=false;Assert.That(list.itemsSource.Count,Is.EqualTo(1));Assert.That(list.itemsSource[0],Is.SameAs(rows[0]));
                fine.value=false;Assert.That(list.itemsSource.Count,Is.Zero);
                whole.value=true;Assert.That(list.itemsSource.Count,Is.EqualTo(1));Assert.That(list.itemsSource[0],Is.SameAs(rows[1]));
                fine.value=true;Assert.That(list.itemsSource.Count,Is.EqualTo(2));
                root.Q<Toggle>("collider-performance-convex").value=true;
                Assert.That(list.itemsSource.Count,Is.EqualTo(1));Assert.That(list.itemsSource[0],Is.SameAs(rows[0]),
                    "A native warning in one column and a cheap convex in another must not jointly satisfy both conditions.");
                Click(root.Q<Button>("collider-select-all"));Assert.That(rows[0].Checked);Assert.That(rows[1].Checked,Is.False);
                Click(root.Q<Button>("collider-performance-filter-All"));Assert.That(list.itemsSource.Count,Is.EqualTo(2));
                root.Q<UnityEditor.UIElements.ToolbarSearchField>("collider-search").value="/b.prefab";
                Assert.That(list.itemsSource.Count,Is.EqualTo(1));Assert.That(list.itemsSource[0],Is.SameAs(rows[1]));
                Assert.That(root.Q<Label>("collider-performance-summary").text,Does.Contain("范围 1 件"));
                foreach(var toggle in sources)toggle.value=false;
                Assert.That(list.itemsSource.Count,Is.Zero);
                Assert.That(root.Q<Label>("collider-performance-summary").text,Does.Contain("未选择检查列"));
                Click(root.Q<Button>("collider-clear-filters"));
                Assert.That(list.itemsSource.Count,Is.EqualTo(2));Assert.That(sources.All(t=>t.value));
                Assert.That(root.Q<Toggle>("collider-performance-convex").value,Is.False);
                Assert.That(root.Q<Button>("collider-performance-filter-All").ClassListContains("collider-tag-active"));
                Assert.That(root.Q<Label>("collider-performance-summary").text,Does.Contain("重点 2"));
                CollectionAssert.AreEqual(firstBytes,File.ReadAllBytes(first));CollectionAssert.AreEqual(reviewBytes,File.ReadAllBytes(window.Workspace.Profile.ReviewFile));
            }
            finally{window.Close();}
        }

        [UnityTest] public IEnumerator VisibleTagsTuneEntryAndFinishReturnPreserveFeedbackAndLayout()
        {
            TuningTestWindow.Adapter = new TuningTestAdapter(id, folder, true);
            var window = ScriptableObject.CreateInstance<TuningTestWindow>(); window.position = new Rect(0, 0, 1100, 800); window.Show();
            try
            {
                window.Workspace.Read(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(first) });
                var row = window.Workspace.Items.Single();
                yield return Await(window.Workspace.GenerateAsync(new[] { row }, ColliderTargetSelection.All));
                window.Workspace.Apply(new[] { row }, ColliderTargetSelection.All);
                for (int i = 0; i < 8; i++) yield return null;
                var root = window.rootVisualElement; var list = root.Q<ListView>();
                Assert.That(root.Query<Button>(className: "collider-tag").ToList().Count, Is.EqualTo(7));
                Assert.That(root.Q<Button>("collider-tag-Pending").ClassListContains("collider-tag-active"));
                Assert.That(root.Query<Image>().ToList().Count, Is.EqualTo(5));
                Assert.That(root.Query<ProgressBar>().ToList().Count, Is.EqualTo(2));
                Assert.That(root.Q<Button>("collider-tune-0"), Is.Not.Null); Assert.That(root.Q<Button>("collider-tune-1"), Is.Not.Null);
                Click(root.Q<Button>("collider-tune-1")); yield return null;
                Assert.That(Resources.FindObjectsOfTypeAll<ColliderItemSettingsWindow>().Length, Is.EqualTo(1));
                Resources.FindObjectsOfTypeAll<ColliderItemSettingsWindow>().Single().Close();
                Assert.That(row.Recipe(1), Is.Null, "opening and cancelling is not an override");
                Assert.That(root.Q<Button>("collider-row-review-action").text, Is.EqualTo("标记完成"));
                Click(root.Q<Button>("collider-row-review-action")); yield return null;
                Assert.That(row.IsConfirmed); Assert.That(list.itemsSource.Count, Is.Zero);
                Click(root.Q<Button>("collider-tag-Done")); for (int i = 0; i < 5; i++) yield return null;
                Assert.That(list.itemsSource.Count, Is.EqualTo(1)); Assert.That(root.Q<Button>("collider-row-review-action").text, Is.EqualTo("标为待处理"));
                Click(root.Q<Button>("collider-row-review-action")); yield return null;
                Assert.That(list.itemsSource.Count, Is.Zero); Assert.That(row.IsConfirmed, Is.False);
                Click(root.Q<Button>("collider-tag-Pending"));
                row.Error = "fixture generation error"; window.Refresh();
                for (int i = 0; i < 5; i++) yield return null;
                Assert.That(root.Query<Label>(className: "collider-row-state").ToList().Any(l => l.text.Contains("fixture generation error") && l.resolvedStyle.display == DisplayStyle.Flex));
                window.position = new Rect(0, 0, 740, 430); for (int i = 0; i < 8; i++) yield return null;
                Assert.That(root.Query<Image>().ToList().All(i => Mathf.Abs(i.resolvedStyle.height - 125) < .1f));
                Assert.That(root.Q<Button>("collider-tag-All").worldBound.xMax, Is.LessThanOrEqualTo(root.worldBound.xMax));
            }
            finally { window.Close(); }
        }
    }
}
