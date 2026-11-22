using UnityEditor;

namespace July.AssetCheck
{
    internal sealed class AudioPlatformSettingsCopyEntry : WebGLPlatformSettingsCopyEntry
    {
        private readonly AudioPlatformSettings.Platform _source, _miniGame;
        internal override bool NeedsCopy { get; }
        internal override PlatformSettingsCopyCell Source =>
            SourceCell(!_source.Overridden, _source.Summary, _source.Details);
        internal override PlatformSettingsCopyCell MiniGame =>
            TargetCell(_miniGame.Overridden, NeedsCopy, _miniGame.Summary, _miniGame.Details);
        internal override PlatformSettingsCopyCell WeixinMiniGame =>
            new PlatformSettingsCopyCell("—", "音频的 MiniGame 与 WeixinMiniGame 共用一份覆盖。");

        internal AudioPlatformSettingsCopyEntry(AudioImporter importer)
            : base(importer.assetPath, AssetCheckKind.Audio, "（音频）")
        {
            _source = AudioPlatformSettings.ReadWebGL(importer);
            _miniGame = AudioPlatformSettings.ReadMiniGame(importer);
            NeedsCopy = !_miniGame.Overridden || !_source.Samples.Equals(_miniGame.Samples);
        }

        internal override void Apply() =>
            AudioPlatformSettings.CopyWebGLToMiniGame((AudioImporter)AssetImporter.GetAtPath(Path));
    }
}
