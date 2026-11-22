using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace July.AssetCheck
{
    internal sealed class TexturePlatformSettingsCopyEntry : WebGLPlatformSettingsCopyEntry
    {
        private readonly bool _isAtlas, _inherited, _copyMiniGame, _copyWeixinMiniGame;
        private readonly TextureImporterPlatformSettings _source, _miniGame, _weixinMiniGame;

        internal override bool NeedsCopy => _copyMiniGame || _copyWeixinMiniGame;
        internal override PlatformSettingsCopyCell Source => SourceCell(_inherited, Summary(_source), Description(_source));
        internal override PlatformSettingsCopyCell MiniGame =>
            TargetCell(_miniGame.overridden, _copyMiniGame, Summary(_miniGame), Description(_miniGame));
        internal override PlatformSettingsCopyCell WeixinMiniGame =>
            TargetCell(_weixinMiniGame.overridden, _copyWeixinMiniGame, Summary(_weixinMiniGame), Description(_weixinMiniGame));

        internal static TexturePlatformSettingsCopyEntry Inspect(TextureImporter importer)
        {
            var source = importer.GetPlatformTextureSettings("WebGL");
            var inherited = !source.overridden;
            if (inherited) importer.GetDefaultPlatformTextureSettings().CopyTo(source);
            return new TexturePlatformSettingsCopyEntry(importer.assetPath, false, inherited, source,
                importer.GetPlatformTextureSettings("MiniGame"), importer.GetPlatformTextureSettings("WeixinMiniGame"));
        }

        internal static TexturePlatformSettingsCopyEntry Inspect(string path, SpriteAtlas atlas) =>
            new TexturePlatformSettingsCopyEntry(path, true, !atlas.GetPlatformSettings("WebGL").overridden,
                AtlasPlatformSettings.Effective(atlas, "WebGL"), atlas.GetPlatformSettings("MiniGame"), atlas.GetPlatformSettings("WeixinMiniGame"));

        private TexturePlatformSettingsCopyEntry(string path, bool isAtlas, bool inherited,
            TextureImporterPlatformSettings source, TextureImporterPlatformSettings miniGame, TextureImporterPlatformSettings weixinMiniGame)
            : base(path, AssetCheckKind.Texture, isAtlas ? "（图集）" : "")
        {
            _isAtlas = isAtlas;
            _inherited = inherited;
            _source = source;
            _miniGame = miniGame;
            _weixinMiniGame = weixinMiniGame;
            _copyMiniGame = !Matches(miniGame);
            _copyWeixinMiniGame = !Matches(weixinMiniGame);
        }

        internal override void Apply()
        {
            if (!_isAtlas)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Path);
                if (_copyMiniGame) TuanjieTextureSettings.WriteSettings(importer, ForTarget("MiniGame"));
                if (_copyWeixinMiniGame) TuanjieTextureSettings.WriteSettings(importer, ForTarget("WeixinMiniGame"));
                importer.SaveAndReimport();
                return;
            }

            var atlas = (SpriteAtlas)AssetDatabase.LoadMainAssetAtPath(Path);
            if (_copyMiniGame) atlas.SetPlatformSettings(ForTarget("MiniGame"));
            if (_copyWeixinMiniGame) atlas.SetPlatformSettings(ForTarget("WeixinMiniGame"));
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssetIfDirty(atlas);
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);
        }

        private TextureImporterPlatformSettings ForTarget(string target)
        {
            var settings = new TextureImporterPlatformSettings();
            _source.CopyTo(settings);
            settings.name = target;
            settings.overridden = true;
            return settings;
        }

        private bool Matches(TextureImporterPlatformSettings current) =>
            JsonUtility.ToJson(ForTarget(current.name)) == JsonUtility.ToJson(current);

        private static string Summary(TextureImporterPlatformSettings settings) =>
            TextureFormatRule.FormatName(settings.format) + $" / {settings.maxTextureSize} / 质量 {settings.compressionQuality}";

        private static string Description(TextureImporterPlatformSettings settings) =>
            $"平台覆盖：{(settings.overridden ? "开启" : "关闭")}\n" +
            $"格式：{TextureFormatRule.FormatName(settings.format)}\n最大尺寸：{settings.maxTextureSize}\n" +
            $"压缩：{settings.textureCompression}\n压缩质量：{settings.compressionQuality}\n缩放算法：{settings.resizeAlgorithm}\n" +
            $"Crunch：{settings.crunchedCompression}\nAlpha 分离：{settings.allowsAlphaSplitting}\n" +
            $"忽略平台支持限制：{settings.ignorePlatformSupport}\nETC2 回退：{settings.androidETC2FallbackOverride}";
    }
}
