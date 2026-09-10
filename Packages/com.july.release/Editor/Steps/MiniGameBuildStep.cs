using System.IO;
using UnityEngine;
namespace July.Release.Editor
{
    public sealed class MiniGameBuildStep : BuildStep
    {
        public override string Name => "平台出包";
        public override string Validate(BuildContext context)
        {
            var error = BuildUtils.ValidateResourceUrls(context, out _, out _, out _);
            if (error != null) return error;
            _ = ReleaseProject.PlatformBuilder(context.Platform);
            return null;
        }
        public override bool Execute(BuildContext context)
        {
            PlatformBuildPaths.CleanExportDirectory(ReleaseConventions.ExportRoot, context.Platform, context.CoreVersion);
            CleanBurstCache();
            PlatformBuildArtifacts artifacts;
            using (TMPFontSwapper.UseLaunchFont())
                artifacts = ReleaseProject.PlatformBuilder(context.Platform).Build(context);
            if (artifacts == null) return false;
            context.Artifacts = artifacts;
            context.DataFilePath = artifacts.DataFilePath;
            return true;
        }
        static void CleanBurstCache()
        {
            var burstOutputDir = Path.Combine("Temp", "BurstOutput");
            if (!Directory.Exists(burstOutputDir)) return;

            Debug.Log($"[MiniGameBuild] 清理 Burst 缓存: {burstOutputDir}");
            Directory.Delete(burstOutputDir, true);
        }
    }
}
