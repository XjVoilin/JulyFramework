using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace July.Release.Editor
{
    /// <summary>
    /// 构建 AssetBundle 并拷贝到本地 CDN 目录。
    /// 写入 ctx.PackageVersion / ABOutputDir / CdnOutputDir / CdnSnapshot* 供后续步骤和 UI 读取。
    /// </summary>
    public sealed class AssetBundleBuildStep : BuildStep
    {
        public override string Name => "AB 构建 + 拷贝到本地 CDN";

        public override string Validate(BuildContext ctx)
        {
            HybridCLRBuildHelper.ValidateCollectorDirectories();
            return null;
        }

        public override bool Execute(BuildContext ctx)
        {
            ctx.PackageVersion = BuildUtils.NewPackageVersion();

            var copyOption = AssetBundleBuilderSetting.GetPackageBuildinFileCopyOption(
                BuildUtils.PackageName, BuildUtils.YooAssetPipelineType);
            var copyParams = AssetBundleBuilderSetting.GetPackageBuildinFileCopyParams(
                BuildUtils.PackageName, BuildUtils.YooAssetPipelineType);

            // ── 构建 AB（临时替换 TMP 字体，避免 Font_Main 通过 TMP Settings 污染依赖图）──
            BuildResult buildResult;
            using (TMPFontSwapper.UseLaunchFont())
                buildResult = RunBuild(ctx, copyOption, copyParams);

            if (!buildResult.Success)
            {
                Debug.LogError($"[AB] 构建失败: {buildResult.ErrorInfo}");
                return false;
            }

            ctx.ABOutputDir = buildResult.OutputPackageDirectory;
            PostBuildSanityCheck(ctx.ABOutputDir);

            // ── 拷贝到 CDN ──
            ctx.CdnOutputDir = BuildUtils.GetCdnPath(ctx.Env, ctx.Platform, ctx.CoreVersion, ctx.PlanVersion);
            ctx.CdnSnapshotBefore = BuildUtils.SnapshotDirectory(ctx.CdnOutputDir);
            BuildUtils.CopyDirectory(ctx.ABOutputDir, ctx.CdnOutputDir);
            ctx.CdnSnapshotAfter = BuildUtils.SnapshotDirectory(ctx.CdnOutputDir);

            Debug.Log($"[AB] 构建完成 Pkg:{ctx.PackageVersion} → {ctx.CdnOutputDir}");
            return true;
        }

        internal static IBuildTask CreateBuildMapTask(bool mergeSharedBundles) =>
            mergeSharedBundles ? (IBuildTask)new CustomTaskGetBuildMap_SBP() : new TaskGetBuildMap_SBP();

        private static BuildResult RunBuild(BuildContext ctx,
            EBuildinFileCopyOption copyOption, string copyParams)
        {
            var outputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot();
            var packageRoot = $"{outputRoot}/{ctx.Target}/{BuildUtils.PackageName}";

            if (Directory.Exists(packageRoot))
            {
                foreach (var old in Directory.GetDirectories(packageRoot))
                {
                    Debug.Log($"[AB] 清理旧构建产物: {old}");
                    Directory.Delete(old, true);
                }
            }

            var buildParams = new ScriptableBuildParameters
            {
                BuildOutputRoot = outputRoot,
                BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = BuildUtils.YooAssetPipelineType,
                BuildBundleType = (int)EBuildBundleType.AssetBundle,
                BuildTarget = ctx.Target,
                PackageName = BuildUtils.PackageName,
                PackageVersion = ctx.PackageVersion,
                EnableSharePackRule = true,
                SingleReferencedPackAlone = false,
                VerifyBuildingResult = true,
                FileNameStyle = AssetBundleBuilderSetting.GetPackageFileNameStyle(
                    BuildUtils.PackageName, BuildUtils.YooAssetPipelineType),
                BuildinFileCopyOption = copyOption,
                BuildinFileCopyParams = copyParams,
                CompressOption = ECompressOption.LZ4,
                ClearBuildCacheFiles = false,
                UseAssetDependencyDB = true,
                BuiltinShadersBundleName = "shared_unity_shaders",
            };

            var pipeline = new List<IBuildTask>
            {
                new TaskPrepare_SBP(),
                CreateBuildMapTask(ReleaseProject.Profile.MergeSharedBundles),
                new TaskBuilding_SBP(),
                new TaskVerifyBuildResult_SBP(),
                new TaskEncryption_SBP(),
                new TaskUpdateBundleInfo_SBP(),
                new TaskCreateManifest_SBP(),
                new TaskCreateReport_SBP(),
                new TaskCreatePackage_SBP(),
                new TaskCopyBuildinFiles_SBP(),
                new TaskCreateCatalog_SBP(),
            };

            var builder = new AssetBundleBuilder();
            return builder.Run(buildParams, pipeline, true);
        }

        private static void PostBuildSanityCheck(string abOutputDir)
        {
            if (!Directory.Exists(abOutputDir)) return;

            var allBundles = Directory.GetFiles(abOutputDir, "*.bundle");
            int shareCount = 0;
            foreach (var f in allBundles)
            {
                if (Path.GetFileName(f).StartsWith("share_"))
                    shareCount++;
            }

            Debug.Log($"[AB 检查] 总包数: {allBundles.Length}, share 包数: {shareCount}");

            if (shareCount > 30)
                Debug.LogWarning($"[AB 检查] share 包数量 {shareCount} 偏高（阈值 30），考虑调整 CustomTaskGetBuildMap_SBP.MergeDepth");

            if (allBundles.Length > 80)
                Debug.LogWarning($"[AB 检查] 总 AB 数量 {allBundles.Length} 偏高（阈值 80），请排查是否有新增碎片");
        }
    }
}
