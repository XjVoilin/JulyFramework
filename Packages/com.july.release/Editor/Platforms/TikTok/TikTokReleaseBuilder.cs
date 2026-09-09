using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using TTSDK.Tool;
using July.Release.Editor;
namespace July.Release.Editor.TikTok
{
    public sealed class TikTokReleaseBuilder : IReleasePlatformBuilder
    {
        [InitializeOnLoadMethod]
        static void Register() => ReleaseProject.RegisterPlatform(PlatformKeys.TikTok, () => new TikTokReleaseBuilder());

        public PlatformBuildArtifacts Build(BuildContext context) => BuildTikTok(context) ? LocateArtifacts(context) : null;
        public PlatformBuildArtifacts LocateArtifacts(BuildContext context) => PlatformBuildPaths.Locate(context,
            PlatformBuildPaths.GetExportDirectory(context));
        static void SyncCdnToStarkSettings(BuildContext ctx)
        {
            var cdnUrl = ctx.CdnUrl.TrimEnd('/');

            var starkSettings = StarkBuilderSettings.LoadSettings();

            if (Uri.TryCreate(cdnUrl, UriKind.Absolute, out var uri))
                starkSettings.urlCacheList = new[] { uri.Host };

            starkSettings.CDN = cdnUrl;
            starkSettings.clearStreamingAssets = true;

            starkSettings.Save();
            Debug.Log($"[MiniGameBuild] 已同步抖音构建设置: urlCacheList=[{uri?.Host}], CDN={cdnUrl}, clearStreamingAssets=true");
        }
        static bool BuildTikTok(BuildContext ctx)
        {
#if JULYGF_DY_MINIGAME && TUANJIE_1_5_OR_NEWER
            Debug.Log("[MiniGameBuild] 开始抖音小游戏出包（团结引擎）...");
            SyncCdnToStarkSettings(ctx);
            var starkSettings = StarkBuilderSettings.LoadSettings();

            var exportPath = Path.GetFullPath(PlatformBuildPaths.GetExportDirectory(ctx));
            starkSettings.OutputDir = exportPath;
            starkSettings.Save();
            Debug.Log($"[MiniGameBuild] 导出路径: {exportPath}");

            var playerSettings = AssetDatabase.LoadAssetAtPath<PlayerSettings>(
                "ProjectSettings/ProjectSettings.asset");
            var result = TTSDK.Tool.API.BuildManager.BuildForTuanjie(
                exportPath, playerSettings);
            if (!string.IsNullOrEmpty(result))
            {
                Debug.Log($"[MiniGameBuild] 抖音小游戏出包成功: {result}");
                return true;
            }

            Debug.LogError("[MiniGameBuild] 抖音小游戏出包失败");
            return false;
#elif JULYGF_DY_MINIGAME
            Debug.Log("[MiniGameBuild] 开始抖音小游戏出包...");
            SyncCdnToStarkSettings(ctx);
            var starkSettings = StarkBuilderSettings.LoadSettings();

            var exportPath = Path.GetFullPath(PlatformBuildPaths.GetExportDirectory(ctx));
            starkSettings.OutputDir = exportPath;
            starkSettings.Save();
            Debug.Log($"[MiniGameBuild] 导出路径: {exportPath}");

            var result = TTSDK.Tool.API.BuildManager
                .Build(Framework.Wasm, false)
                .GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(result))
            {
                Debug.Log($"[MiniGameBuild] 抖音小游戏出包成功: {result}");
                return true;
            }

            Debug.LogError("[MiniGameBuild] 抖音小游戏出包失败");
            return false;
#else
            Debug.LogError("[MiniGameBuild] 当前未定义 JULYGF_DY_MINIGAME，无法执行抖音出包");
            return false;
#endif
        }
    }
}
