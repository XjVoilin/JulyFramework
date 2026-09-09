using System.Collections.Generic;

namespace July.Release.Editor
{
    public enum ReleaseBuildMode { Full, HotUpdate }

    /// <summary>面板当前选择。预览与执行使用同一套版本和步骤规则。</summary>
    public sealed class BuildToolSelection
    {
        public ReleaseBuildMode Mode;
        public bool QA;
        public bool Upload;
        public bool CustomSteps;
        public bool HybridCLR = true;
        public bool AssetBundles = true;
        public bool MiniGame = true;
        public string AotBaseline;

        public bool BuildBundles => !CustomSteps || AssetBundles;
        public bool ExportPlayer => Mode == ReleaseBuildMode.Full && (!CustomSteps || MiniGame);

        public void ApplyTo(BuildContext context)
        {
            context.AOTBackupVersion = Mode == ReleaseBuildMode.HotUpdate ? AotBaseline : null;
            context.CoreVersion = Mode == ReleaseBuildMode.Full ? context.PlanVersion : AotBaseline;
            context.IsQABuild = QA;
            if (QA)
                context.CoreVersion = context.PlanVersion = BuildUtils.QAPlanVersion;
        }

        public List<BuildStep> CreateSteps() => Mode == ReleaseBuildMode.Full
            ? PipelinePresets.FullBuild(!CustomSteps || HybridCLR, BuildBundles, Upload, ExportPlayer)
            : PipelinePresets.HotUpdate(BuildBundles, Upload);
    }
}
