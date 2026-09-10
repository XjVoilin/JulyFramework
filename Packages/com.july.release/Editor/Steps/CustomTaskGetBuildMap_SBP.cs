using System;
using System.Collections.Generic;
using System.IO;
using YooAsset.Editor;
using YooBuildContext = YooAsset.Editor.BuildContext;

namespace July.Release.Editor
{
    /// <summary>
    /// 自定义共享资源打包：按目录深度截断合并碎片 share 包，减少 HTTP 请求数。
    /// 默认策略（TaskGetBuildMap）使用 Path.GetDirectoryName 逐目录拆分，
    /// 导致大量小碎片 share 包。本类将共享资源按前 N 层目录分组合并。
    ///
    /// MergeDepth 调节粒度：
    ///   3 → Assets/Game/Art（粗粒度，~5 个 share 包）
    ///   4 → Assets/Game/Art/Textures、Assets/Game/MiniGames/Game101（中粒度，~15 个）
    ///   越大越接近默认行为
    /// </summary>
    public class CustomTaskGetBuildMap_SBP : TaskGetBuildMap, IBuildTask
    {
        private static int MergeDepth => ReleaseProject.Profile.SharedBundleMergeDepth;
        // 字体按用途目录隔离，避免 Privacy/Dynamic 等可延迟资源进入 Lobby 共享包。
        private static int FontMergeDepth => ReleaseProject.Profile.FontMergeDepth;
        private static string FontRoot => ReleaseProject.Profile.FontRoot;

        void IBuildTask.Run(YooBuildContext context)
        {
            var buildParametersContext = context.GetContextObject<BuildParametersContext>();
            var buildMapContext = CreateBuildMap(false, buildParametersContext.Parameters);
            context.SetContextObject(buildMapContext);
        }

        protected override void ProcessingPackShareBundle(
            BuildParameters buildParameters, CollectCommand command, BuildAssetInfo buildAssetInfo)
        {
            string assetPath = buildAssetInfo.AssetInfo.AssetPath;
            string mergeRoot = GetMergeRoot(assetPath);

            var packRuleResult = new PackRuleResult(mergeRoot, DefaultPackRule.AssetBundleFileExtension);
            if (packRuleResult.IsValid() == false)
                return;

            if (buildAssetInfo.GetReferenceBundleCount() <= 1)
            {
                if (buildParameters.SingleReferencedPackAlone == false)
                    return;
            }

            string shareBundleName = packRuleResult.GetShareBundleName(
                command.PackageName, command.UniqueBundleName);
            buildAssetInfo.SetBundleName(shareBundleName);
        }

        private static string GetMergeRoot(string assetPath)
        {
            string dir = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir))
                return assetPath;

            dir = dir.Replace('\\', '/');
            string[] segments = dir.Split('/');
            int configuredDepth = !string.IsNullOrEmpty(FontRoot) && assetPath.StartsWith(
                FontRoot, StringComparison.OrdinalIgnoreCase)
                ? FontMergeDepth
                : MergeDepth;
            int depth = Math.Min(configuredDepth, segments.Length);
            return string.Join("/", segments, 0, depth);
        }
    }
}
