using System.Collections.Generic;
using July.Build;

namespace July.Release.Editor
{
    /// <summary>Release build inputs and outputs carried by July.Build.</summary>
    public sealed class BuildContext : July.Build.BuildContext
    {
        public string Env
        {
            get => Environment;
            set => Environment = value;
        }

        public string CoreVersion { get; set; }

        public string PlanVersion
        {
            get => Version;
            set => Version = value;
        }

        public bool Development { get; set; }
        public bool StrictMetadataCheck { get; set; }
        public string CloudUrl { get; set; }
        public string CdnUrl { get; set; }
        public string AOTBackupVersion { get; set; }
        public bool IsQABuild { get; set; }
        /// <summary>全量构建的持久备份输出绝对目录；null 使用本地归档规则。</summary>
        public string AotBackupOutputPath { get; set; }
        /// <summary>热更必须使用的持久备份绝对目录；null 使用本地恢复规则。</summary>
        public string AotBackupInputPath { get; set; }
        internal AotBackupSnapshot ExplicitAotBackup { get; set; }
        internal bool ExplicitAotBackupRestored { get; set; }
        /// <summary>显式允许同版本/较低版本资源覆盖；每次构建默认关闭。</summary>
        public bool ForceRebuild { get; set; } = false;

        public string PackageVersion { get; set; }
        public string ABOutputDir { get; set; }
        public string CdnOutputDir { get; set; }
        public Dictionary<string, long> CdnSnapshotBefore { get; set; }
        public Dictionary<string, long> CdnSnapshotAfter { get; set; }
        public string DataFilePath { get; set; }
        public PlatformBuildArtifacts Artifacts { get; set; }

        /// <summary>单步重跑或热更沿用已有主包；显式 AOT 基线优先于项目当前版本。</summary>
        internal void UseExistingCoreVersion(string currentCoreVersion)
        {
            CoreVersion = string.IsNullOrEmpty(AOTBackupVersion) ? currentCoreVersion : AOTBackupVersion;
        }

        public override string Validate()
        {
            if (string.IsNullOrWhiteSpace(Platform))
                return "Target platform is required.";
            if (string.IsNullOrWhiteSpace(PlanVersion))
                return "Plan version is required.";
            if (!BuildUtils.IsValidVersion(PlanVersion))
                return $"Plan version is invalid: {PlanVersion}";
            if (string.IsNullOrWhiteSpace(CoreVersion))
                return "Core version is required.";
            if (!BuildUtils.IsValidVersion(CoreVersion))
                return $"Core version is invalid: {CoreVersion}";
            return null;
        }
    }

    /// <summary>
    /// Typed project adapter: steps keep a release context while the framework owns
    /// validation order, execution, result handling and editor lifecycle.
    /// </summary>
    public abstract class BuildStep : July.Build.IBuildStep
    {
        public abstract string Name { get; }
        public abstract string Validate(BuildContext context);
        public abstract bool Execute(BuildContext context);

        string July.Build.IBuildStep.Validate(July.Build.BuildContext context) =>
            context is BuildContext projectContext
                ? Validate(projectContext)
                : "The build step requires a release build context.";

        BuildStepResult July.Build.IBuildStep.Execute(July.Build.BuildContext context)
        {
            if (context is not BuildContext projectContext)
                return BuildStepResult.Failure(
                    "The build step requires a release build context.");

            return Execute(projectContext)
                ? BuildStepResult.Success()
                : BuildStepResult.Failure($"{Name} failed. See the Unity Console for details.");
        }
    }
}
