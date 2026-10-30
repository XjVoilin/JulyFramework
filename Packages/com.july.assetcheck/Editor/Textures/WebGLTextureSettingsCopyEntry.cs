using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    internal sealed class WebGLTextureSettingsCopyEntry
    {
        internal string Path { get; }
        internal string Guid { get; }
        internal bool IsAtlas { get; }
        internal bool Inherited { get; }
        internal TextureImporterPlatformSettings Source { get; }
        internal TextureImporterPlatformSettings MiniGame { get; }
        internal TextureImporterPlatformSettings WeixinMiniGame { get; }
        internal bool CopyMiniGame { get; }
        internal bool CopyWeixinMiniGame { get; }
        internal bool NeedsCopy => CopyMiniGame || CopyWeixinMiniGame;

        internal WebGLTextureSettingsCopyEntry(string path, bool isAtlas, bool inherited,
            TextureImporterPlatformSettings source, TextureImporterPlatformSettings miniGame,
            TextureImporterPlatformSettings weixinMiniGame)
        {
            Path = path;
            Guid = AssetDatabase.AssetPathToGUID(path);
            IsAtlas = isAtlas;
            Inherited = inherited;
            Source = source;
            MiniGame = miniGame;
            WeixinMiniGame = weixinMiniGame;
            CopyMiniGame = !Matches(miniGame);
            CopyWeixinMiniGame = !Matches(weixinMiniGame);
        }

        internal TextureImporterPlatformSettings ForTarget(string target)
        {
            var settings = new TextureImporterPlatformSettings();
            Source.CopyTo(settings);
            settings.name = target;
            settings.overridden = true;
            return settings;
        }

        private bool Matches(TextureImporterPlatformSettings target) =>
            JsonUtility.ToJson(ForTarget(target.name)) == JsonUtility.ToJson(target);

        internal static string Summary(TextureImporterPlatformSettings settings) =>
            TextureFormatRule.FormatName(settings.format) + $" / {settings.maxTextureSize} / 质量 {settings.compressionQuality}";

        internal static string Description(TextureImporterPlatformSettings settings) =>
            $"平台覆盖：{(settings.overridden ? "开启" : "关闭")}\n" +
            $"格式：{TextureFormatRule.FormatName(settings.format)}\n最大尺寸：{settings.maxTextureSize}\n" +
            $"压缩：{settings.textureCompression}\n压缩质量：{settings.compressionQuality}\n缩放算法：{settings.resizeAlgorithm}\n" +
            $"Crunch：{settings.crunchedCompression}\nAlpha 分离：{settings.allowsAlphaSplitting}\n" +
            $"忽略平台支持限制：{settings.ignorePlatformSupport}\nETC2 回退：{settings.androidETC2FallbackOverride}";
    }
}
