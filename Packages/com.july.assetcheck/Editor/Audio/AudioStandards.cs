using System;
using System.IO;

namespace July.AssetCheck
{
    [Flags]
    public enum AudioLoadTypes
    {
        Default = 0,
        DecompressOnLoad = 1,
        CompressedInMemory = 2,
        Streaming = 4
    }

    [Serializable]
    public sealed class AudioStandards
    {
        public int maxSampleRate = 44100;
        public int maxChannels = 2;
        public bool requireMiniGameOverride;
        public AudioLoadTypes bgmLoadTypes;
        public AudioLoadTypes sfxLoadTypes;

        internal AudioLoadTypes BgmLoadTypes => bgmLoadTypes == AudioLoadTypes.Default
            ? AudioLoadTypes.CompressedInMemory : bgmLoadTypes;
        internal AudioLoadTypes SfxLoadTypes => sfxLoadTypes == AudioLoadTypes.Default
            ? AudioLoadTypes.DecompressOnLoad | AudioLoadTypes.CompressedInMemory : sfxLoadTypes;

        internal void Validate()
        {
            if (maxSampleRate <= 0 || maxChannels < 1 || maxChannels > 8)
                throw new InvalidDataException("音频采样率必须为正数，声道上限必须在 1 到 8 之间。");
            const AudioLoadTypes supportedLoadTypes = AudioLoadTypes.DecompressOnLoad |
                AudioLoadTypes.CompressedInMemory | AudioLoadTypes.Streaming;
            if (((bgmLoadTypes | sfxLoadTypes) & ~supportedLoadTypes) != 0)
                throw new InvalidDataException("音频标准包含未知的加载方式。");
        }
    }
}
