using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using July.Release.Editor;
namespace July.Release.Editor.WeChat
{
    public sealed class WeChatReleaseBuilder : IReleasePlatformBuilder
    {
        [InitializeOnLoadMethod]
        static void Register() => ReleaseProject.RegisterPlatform(PlatformKeys.WeChat, () => new WeChatReleaseBuilder());

        public PlatformBuildArtifacts Build(BuildContext context) => BuildWeChat(context) ? LocateArtifacts(context) : null;
        public PlatformBuildArtifacts LocateArtifacts(BuildContext context)
        {
            var root = PlatformBuildPaths.GetExportDirectory(context);
            return PlatformBuildPaths.Locate(context, root, Path.Combine(root, WeChatWASM.WXConvertCore.webglDir));
        }
        static bool BuildWeChat(BuildContext ctx)
        {
#if JULYGF_WX_MINIGAME
            var wxConfig = WeChatWASM.WXConvertCore.config;

            var exportPath = PlatformBuildPaths.GetExportDirectory(ctx);
            wxConfig.ProjectConf.relativeDST = Path.GetRelativePath(Path.GetFullPath("."), exportPath).Replace('\\', '/');
            wxConfig.ProjectConf.DST = exportPath;
            Debug.Log($"[MiniGameBuild] 导出路径: {exportPath}");

            wxConfig.ProjectConf.assetLoadType = 0;
            wxConfig.ProjectConf.CDN = ctx.CdnUrl.TrimEnd('/');
            wxConfig.ProjectConf.dataFileSubPrefix = $"{ctx.Env}/{ctx.Platform}/{ctx.CoreVersion}/";

            if (File.Exists(ReleaseProject.Profile.SplashImagePath))
            {
                wxConfig.ProjectConf.bgImageSrc = ReleaseProject.Profile.SplashImagePath;
                Debug.Log($"[MiniGameBuild] 启动背景图: {ReleaseProject.Profile.SplashImagePath}");
            }
            else
            {
                Debug.LogWarning($"[MiniGameBuild] 启动背景图不存在: {ReleaseProject.Profile.SplashImagePath}，沿用当前配置");
            }

            EditorUtility.SetDirty(wxConfig);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[MiniGameBuild] 已配置微信首包为 CDN 模式: assetLoadType=0, CDN={wxConfig.ProjectConf.CDN}, dataFileSubPrefix={wxConfig.ProjectConf.dataFileSubPrefix}");

            var originalDevelopment = wxConfig.CompileOptions.DevelopBuild;
            var originalPerfAnalysis = wxConfig.CompileOptions.enablePerfAnalysis;
            try
            {
                // 普通构建也显式关闭性能集成，避免沿用 SDK 面板残留的配置。
                wxConfig.CompileOptions.enablePerfAnalysis = ctx.WeChatPerfAnalysis;
                if (ctx.WeChatPerfAnalysis) wxConfig.CompileOptions.DevelopBuild = true;
                EditorUtility.SetDirty(wxConfig);
                AssetDatabase.SaveAssets();
                Debug.Log($"[MiniGameBuild] 开始微信小游戏出包：Development={wxConfig.CompileOptions.DevelopBuild}, Perf={ctx.WeChatPerfAnalysis}");
                var error = WeChatWASM.WXConvertCore.DoExport();
                if ((int)error == 0)
                {
                    Debug.Log("[MiniGameBuild] 微信小游戏出包成功");
                    return true;
                }

                Debug.LogError($"[MiniGameBuild] 微信小游戏出包失败: {error}");
                return false;
            }
            finally
            {
                wxConfig.CompileOptions.DevelopBuild = originalDevelopment;
                wxConfig.CompileOptions.enablePerfAnalysis = originalPerfAnalysis;
                EditorUtility.SetDirty(wxConfig);
                AssetDatabase.SaveAssets();
            }
#else
            Debug.LogError("[MiniGameBuild] 当前未定义 JULYGF_WX_MINIGAME，无法执行微信出包");
            return false;
#endif
        }
    }
}
