using System;
using System.Linq;
using July.Logging;
using July.Release.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace July.AssetCheck
{
    internal enum BundleAnalysisBuildStage { WaitingForReload, Ready, Building, Succeeded, Failed, Cancelled }

    [Serializable]
    internal sealed class BundleAnalysisBuildRequest
    {
        public BuildTarget target;
        public BundleAnalysisBuildStage stage;
        public string reportPath, error;
        internal bool Running => stage == BundleAnalysisBuildStage.WaitingForReload || stage == BundleAnalysisBuildStage.Ready || stage == BundleAnalysisBuildStage.Building;
        internal bool CanCancel => stage == BundleAnalysisBuildStage.WaitingForReload || stage == BundleAnalysisBuildStage.Ready;

        internal BundleAnalysisBuildRequest(BuildTarget target, BuildTarget active)
        { this.target = target; stage = target == active ? BundleAnalysisBuildStage.Ready : BundleAnalysisBuildStage.WaitingForReload; }

        internal void AfterReload()
        {
            if (stage == BundleAnalysisBuildStage.WaitingForReload) stage = BundleAnalysisBuildStage.Ready;
            else if (stage == BundleAnalysisBuildStage.Building) Fail("构建期间发生脚本重载，分析构建已中断，请重新发起。");
        }
        internal bool TryBegin(BuildTarget active, bool editorBusy, bool compilationFailed)
        {
            if (!Running || stage == BundleAnalysisBuildStage.Building || editorBusy) return false;
            if (compilationFailed) { Fail("脚本编译失败，未执行分析构建。请修复编译错误后重新发起。"); return false; }
            if (stage != BundleAnalysisBuildStage.Ready) return false;
            if (active != target) { Fail("当前平台与请求的 " + target + " 不一致，未执行分析构建。"); return false; }
            stage = BundleAnalysisBuildStage.Building;
            return true;
        }
        internal void Fail(string message) { stage = BundleAnalysisBuildStage.Failed; error = message; }
        internal void Cancel()
        {
            if (!CanCancel) throw new InvalidOperationException("当前阶段不能取消后续构建。");
            stage = BundleAnalysisBuildStage.Cancelled;
        }
    }

    // One editor-session request survives script reload; closing/recreating the view does not own its lifetime.
    [InitializeOnLoad]
    internal static class BundleAnalysisBuildQueue
    {
        static string Key => "July.AssetCheck.AnalysisBuild." + Application.dataPath;
        internal static BundleAnalysisBuildRequest Current { get; private set; }
        internal static bool Running => Current != null && Current.Running;
        internal static event Action Changed;

        static BundleAnalysisBuildQueue()
        {
            var json = SessionState.GetString(Key, "");
            if (json.Length > 0) { Current = JsonUtility.FromJson<BundleAnalysisBuildRequest>(json); Current.AfterReload(); Save(); }
            EditorApplication.update += Update;
        }

        internal static BuildTarget[] SupportedTargets() => Enum.GetValues(typeof(BuildTarget)).Cast<BuildTarget>().Distinct()
            .Where(target => BuildPipeline.GetBuildTargetGroup(target) != BuildTargetGroup.Unknown &&
                BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
            .OrderBy(target => target.ToString(), StringComparer.Ordinal).ToArray();

        internal static void Start(BuildTarget target)
        {
            if (Running || ReleaseAssetBundleBuilder.IsBuilding || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("已有构建待办或构建正在进行。");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请等待编辑器完成导入、编译并退出播放模式。");
            if (EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("请先修复脚本编译错误。");
            if (!SupportedTargets().Contains(target)) throw new InvalidOperationException("当前引擎未安装目标平台支持：" + target);
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存当前场景，再构建分析 AB。");
            var active = EditorUserBuildSettings.activeBuildTarget;
            if (target != active && Application.isBatchMode) throw new InvalidOperationException("批处理模式请通过 -buildTarget 启动目标平台，不能在会话内切换。");
            Current = new BundleAnalysisBuildRequest(target, active);
            Save(); Changed?.Invoke();
            if (target == active) return;
            try
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildPipeline.GetBuildTargetGroup(target), target))
                    Fail("无法切换到 " + target + "，未执行分析构建。");
            }
            catch (Exception exception) { Fail(exception.Message); JLogger.LogException(exception); }
        }

        static void Update()
        {
            if (!Running) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (Current.CanCancel) { Current.Cancel(); Save(); Changed?.Invoke(); }
                return;
            }
            var previous = Current.stage;
            var begin = Current.TryBegin(EditorUserBuildSettings.activeBuildTarget,
                EditorApplication.isCompiling || EditorApplication.isUpdating, EditorUtility.scriptCompilationFailed);
            if (Current.stage != previous) { Save(); Changed?.Invoke(); }
            if (!begin) return;
            try
            {
                var result = ReleaseAssetBundleBuilder.BuildAnalysis(Current.target);
                Current.reportPath = result.ReportPath;
                Current.stage = BundleAnalysisBuildStage.Succeeded;
            }
            catch (Exception exception) { Current.Fail(exception.Message); JLogger.LogException(exception); }
            finally { EditorUtility.ClearProgressBar(); Save(); Changed?.Invoke(); }
        }

        internal static void Cancel() { Current.Cancel(); Save(); Changed?.Invoke(); }
        internal static string TakeReport()
        {
            if (Current == null || Current.stage != BundleAnalysisBuildStage.Succeeded) return null;
            var path = Current.reportPath; ClearResult(); return path;
        }
        internal static void ClearResult()
        {
            if (Running) throw new InvalidOperationException("不能清除进行中的分析构建。");
            Current = null; SessionState.EraseString(Key);
        }
        static void Fail(string message) { Current.Fail(message); Save(); Changed?.Invoke(); }
        static void Save() => SessionState.SetString(Key, JsonUtility.ToJson(Current));
    }
}
