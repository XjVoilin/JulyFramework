using System.Text.RegularExpressions;
using UnityEditor;

namespace July.Release.Editor
{
    /// <summary>
    /// 全量构建路径：GenerateAll（编译 DLL + IL2CPP 构建 + AOT 泛型分析）→ 拷贝 DLL 到资源目录。
    /// 确保 AOT 裁剪 DLL 和 AOTGenericReferences 与当前代码同步。
    /// </summary>
    public sealed class HybridCLRGenerateAllStep : BuildStep
    {
        public override string Name => "HybridCLR Generate All + 拷贝 DLL";

        public override string Validate(BuildContext ctx)
        {
            return HybridCLRBuildHelper.ValidateSettings() ? null : "HybridCLR 配置校验失败";
        }

        public override bool Execute(BuildContext ctx)
        {
            // GenerateAll 内部读取全局 development，必须在编译 DLL 和裁剪 AOT 之前应用。
            var originalDevelopment = EditorUserBuildSettings.development;
            var weChatPerf = ctx.Platform == PlatformKeys.WeChat && ctx.WeChatPerfAnalysis;
            var originalArgs = weChatPerf ? EmscriptenArgs : null;
            try
            {
                if (weChatPerf)
                {
                    EditorUserBuildSettings.development = true;
                    // AOT 临时 Player 先于微信 SDK DoExport 构建，此时性能插件尚未准备。
                    EmscriptenArgs = PrepareWeChatAotArgs(originalArgs);
                }
                return HybridCLRBuildHelper.GenerateAllAndCopyDlls(ctx.Target);
            }
            finally
            {
                EditorUserBuildSettings.development = originalDevelopment;
                if (weChatPerf) EmscriptenArgs = originalArgs;
            }
        }

        internal static string PrepareWeChatAotArgs(string args)
        {
            var remaining = Regex.Replace(args ?? string.Empty,
                @"(?<!\S)-s\s*ERROR_ON_UNDEFINED_SYMBOLS=\S+", string.Empty).Trim();
            const string setting = "-s ERROR_ON_UNDEFINED_SYMBOLS=0";
            return remaining.Length == 0 ? setting : remaining + " " + setting;
        }

        static string EmscriptenArgs
        {
#if TUANJIE_1_5_OR_NEWER
            get => PlayerSettings.MiniGame.emscriptenArgs;
            set => PlayerSettings.MiniGame.emscriptenArgs = value;
#else
            get => PlayerSettings.WebGL.emscriptenArgs;
            set => PlayerSettings.WebGL.emscriptenArgs = value;
#endif
        }
    }
}
