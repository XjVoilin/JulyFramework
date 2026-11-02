using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class BundleAnalysisBuildQueueTests
    {
        static BundleAnalysisBuildRequest Reload(BundleAnalysisBuildRequest request)
        {
            var restored = JsonUtility.FromJson<BundleAnalysisBuildRequest>(JsonUtility.ToJson(request));
            restored.AfterReload(); return restored;
        }
        [Test] public void CurrentTargetNeedsNoReloadButWaitsForIdleEditor()
        {
            var request = new BundleAnalysisBuildRequest(BuildTarget.WebGL, BuildTarget.WebGL);
            Assert.AreEqual(BundleAnalysisBuildStage.Ready, request.stage);
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, true, false));
            Assert.IsTrue(request.TryBegin(BuildTarget.WebGL, false, false));
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, false, false));
        }
        [Test] public void ChangedTargetCannotBuildInOldDomainEvenAfterPlatformHasChanged()
        {
            var request = new BundleAnalysisBuildRequest(BuildTarget.WebGL, BuildTarget.StandaloneWindows64);
            Assert.IsTrue(request.Running); Assert.IsTrue(request.CanCancel);
            Assert.IsFalse(request.TryBegin(BuildTarget.StandaloneWindows64, false, false));
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, true, false));
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, false, false));
            Assert.AreEqual(BundleAnalysisBuildStage.WaitingForReload, request.stage);
        }
        [Test] public void TargetAndRequestSurviveReloadAndOnlyStartAfterCompilationAndImport()
        {
            var request = Reload(new BundleAnalysisBuildRequest(BuildTarget.WebGL, BuildTarget.StandaloneWindows64));
            Assert.AreEqual(BuildTarget.WebGL, request.target);
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, true, false));
            Assert.IsTrue(request.TryBegin(BuildTarget.WebGL, false, false));
            Assert.IsFalse(request.CanCancel);
        }
        [Test] public void DifferentTargetAfterReloadFailsWithoutBuilding()
        {
            var request = Reload(new BundleAnalysisBuildRequest(BuildTarget.WebGL, BuildTarget.StandaloneWindows64));
            Assert.IsFalse(request.TryBegin(BuildTarget.StandaloneWindows64, false, false));
            Assert.AreEqual(BundleAnalysisBuildStage.Failed, request.stage); Assert.IsFalse(request.Running);
            Assert.That(request.error, Does.Contain("WebGL"));
        }
        [TestCase(false)] [TestCase(true)]
        public void CompilationErrorTerminatesSameTargetAndSwitchRequests(bool switching)
        {
            var request = new BundleAnalysisBuildRequest(BuildTarget.WebGL, switching ? BuildTarget.StandaloneWindows64 : BuildTarget.WebGL);
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, false, true));
            Assert.AreEqual(BundleAnalysisBuildStage.Failed, request.stage);
            request = Reload(request);
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, false, false));
        }
        [TestCase(false)] [TestCase(true)]
        public void CancellationSurvivesReloadAndNeverBuilds(bool switching)
        {
            var request = new BundleAnalysisBuildRequest(BuildTarget.WebGL, switching ? BuildTarget.StandaloneWindows64 : BuildTarget.WebGL);
            request.Cancel(); request = Reload(request);
            Assert.AreEqual(BundleAnalysisBuildStage.Cancelled, request.stage);
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, false, false)); Assert.IsFalse(request.Running);
        }
        [Test] public void InterruptedBuildFailsAfterReloadRatherThanRetrying()
        {
            var request = new BundleAnalysisBuildRequest(BuildTarget.WebGL, BuildTarget.WebGL);
            Assert.IsTrue(request.TryBegin(BuildTarget.WebGL, false, false));
            Assert.Throws<InvalidOperationException>(() => request.Cancel());
            request = Reload(request);
            Assert.AreEqual(BundleAnalysisBuildStage.Failed, request.stage);
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, false, false));
        }
        [Test] public void SuccessfulReportRemainsAvailableAfterViewOrDomainRecreation()
        {
            var request = new BundleAnalysisBuildRequest(BuildTarget.WebGL, BuildTarget.WebGL)
            { stage = BundleAnalysisBuildStage.Succeeded, reportPath = "Temp/result.report" };
            request = Reload(request);
            Assert.AreEqual("Temp/result.report", request.reportPath);
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, false, false));
        }
        [Test] public void SwitchFailureSurvivesReloadWithoutRetry()
        {
            var request = new BundleAnalysisBuildRequest(BuildTarget.WebGL, BuildTarget.StandaloneWindows64);
            request.Fail("switch failed"); request = Reload(request);
            Assert.AreEqual("switch failed", request.error);
            Assert.IsFalse(request.TryBegin(BuildTarget.WebGL, false, false));
        }
        [Test] public void SupportedTargetsAreInstalledAndDoNotRepeatEnumAliases()
        {
            var targets = BundleAnalysisBuildQueue.SupportedTargets();
            Assert.IsNotEmpty(targets);
            Assert.AreEqual(targets.Length, targets.Distinct().Count());
            Assert.IsTrue(targets.All(target => BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target)));
        }
        [Test] public void ViewRecreationPreservesRequestAndCancelUnlocksControlsWithoutBuilding()
        {
            var temp = Path.GetFullPath("Temp/JulyBuildLifetime_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(temp)))
            {
                BundleAnalysisPanel panel = null;
                try
                {
                    BundleAnalysisBuildQueue.Start(EditorUserBuildSettings.activeBuildTarget);
                    Assert.Throws<InvalidOperationException>(() => BundleAnalysisBuildQueue.Start(EditorUserBuildSettings.activeBuildTarget));
                    panel = new BundleAnalysisPanel(() => workspace, () => { });
                    Assert.IsTrue(panel.Running); Assert.IsFalse(panel.Q<Button>("build-analysis-bundles").enabledSelf);
                    panel.Dispose(); panel = null;
                    Assert.IsTrue(BundleAnalysisBuildQueue.Running);
                    panel = new BundleAnalysisPanel(() => workspace, () => { });
                    Assert.IsTrue(panel.Running);
                    BundleAnalysisBuildQueue.Cancel();
                    Assert.IsFalse(panel.Running); Assert.IsTrue(panel.Q<Button>("build-analysis-bundles").enabledSelf);
                    Assert.That(panel.Q<Label>("analysis-status").text, Does.Contain("已取消"));
                    Assert.IsNull(BundleAnalysisBuildQueue.TakeReport());
                }
                finally
                {
                    panel?.Dispose();
                    if (BundleAnalysisBuildQueue.Current?.CanCancel == true) BundleAnalysisBuildQueue.Cancel();
                    BundleAnalysisBuildQueue.ClearResult();
                }
            }
            Assert.That(temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar)); Directory.Delete(temp, true);
        }
        [UnityTest] public IEnumerator SelectorKeepsBuildActionFixedAndExplainsEverySelectedTarget()
        {
            var temp = Path.GetFullPath("Temp/JulyBuildTarget_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(temp)))
            {
                var host = ScriptableObject.CreateInstance<EditorWindow>();
                using (var panel = new BundleAnalysisPanel(() => workspace, () => { }))
                {
                    try
                    {
                        host.position = new Rect(0, 0, 1050, 600); host.Show(); host.rootVisualElement.Add(panel);
                        yield return null;
                        var target = panel.Q<PopupField<BuildTarget>>("analysis-build-target");
                        var build = panel.Q<Button>("build-analysis-bundles");
                        var hint = panel.Q<Label>("analysis-active-target");
                        Assert.AreEqual(EditorUserBuildSettings.activeBuildTarget, target.value);
                        Assert.AreEqual("构建 AB 并分析", build.text);
                        Assert.That(hint.text, Does.Contain("使用当前平台构建"));
                        foreach (var choice in target.choices)
                        {
                            target.value = choice;
                            yield return null;
                            Assert.AreEqual("构建 AB 并分析", build.text);
                            Assert.That(hint.text, Does.Contain("当前平台：" + EditorUserBuildSettings.activeBuildTarget));
                            if (choice == EditorUserBuildSettings.activeBuildTarget) Assert.That(hint.text, Does.Contain("使用当前平台构建").And.Not.Contain("将切换"));
                            else Assert.That(hint.text, Does.Contain("将切换到 " + choice + "，编译完成后自动构建"));
                            Assert.IsFalse(BundleAnalysisBuildQueue.Running, "仅选择目标不能切平台或提交构建。");
                            Assert.AreEqual(choice, target.value);
                            Assert.Greater(hint.worldBound.width, 0);
                            Assert.LessOrEqual(hint.worldBound.xMax, build.worldBound.xMin + 1);
                            Assert.LessOrEqual(build.worldBound.xMax, host.rootVisualElement.worldBound.xMax + 1);
                        }
                        target.value = EditorUserBuildSettings.activeBuildTarget;
                        Assert.AreEqual("构建 AB 并分析", build.text);
                        Assert.That(hint.text, Does.Contain("使用当前平台构建").And.Not.Contain("将切换"));
                    }
                    finally { Object.DestroyImmediate(host); }
                }
            }
            Assert.That(temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar)); Directory.Delete(temp, true);
        }
    }
}
