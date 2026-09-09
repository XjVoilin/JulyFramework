using System.Collections.Generic;

namespace July.Release.Editor
{
    /// <summary>
    /// 预定义的流水线步骤配方。Window 和 CI 共用，确保编排逻辑只在一处维护。
    /// </summary>
    public static class PipelinePresets
    {
        public static List<BuildStep> FullBuild(
            bool hybridCLR = true, bool ab = true,
            bool upload = false, bool miniGame = false)
        {
            var steps = new List<BuildStep>
            {
                new PlatformDefinesValidationStep(),
            };
            if (hybridCLR)
            {
                steps.Add(new HybridCLRInstallStep());
                steps.Add(new HybridCLRGenerateAllStep());
            }
            if (ab)
            {
                steps.Add(new AssetBundleBuildStep());
                steps.Add(new AOTBackupStep());
                steps.Add(new AOTBackupArchiveStep());
            }

            if (upload) steps.Add(new CloudUploadStep());
            if (miniGame) steps.Add(new WebGLDebugSymbolStep());
            if (miniGame) steps.Add(new MiniGameBuildStep());
            if (miniGame && upload) steps.Add(new DataFileUploadStep());
            if (miniGame) steps.Add(new PreloadInjectionStep(upload));
            if (upload) steps.Add(new GitTagStep());     // CDN 上传成功后归档
            return steps;
        }

        public static List<BuildStep> HotUpdate(bool ab = true, bool upload = false)
        {
            var steps = new List<BuildStep>
            {
                new PlatformDefinesValidationStep(),
                new AOTBackupRestoreStep(),
                new AotSourceHashStep(),
                new HybridCLRHotUpdateStep(),
            };
            if (ab) steps.Add(new AssetBundleBuildStep());
            if (upload) steps.Add(new CloudUploadStep());
            if (upload) steps.Add(new PreloadJsonUpdateStep());
            if (upload) steps.Add(new GitTagStep());
            return steps;
        }
    }
}