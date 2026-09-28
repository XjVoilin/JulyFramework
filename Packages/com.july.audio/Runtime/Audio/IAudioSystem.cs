using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Audio
{
    public interface IAudioSystem
    {
        #region BGM

        void PlayBGM(string fileName, BGMPlayOptions options = null);
        UniTask<bool> PlayBGMAsync(string fileName, BGMPlayOptions options = null, CancellationToken ct = default);
        void StopBGM(float fadeOutDuration = 0f);
        void PauseBGM();
        void ResumeBGM();
        bool IsBGMPlaying();
        AudioHandle GetCurrentBGMHandle();

        #endregion

        #region SFX

        /// <summary>播放音效。2D、3D 同名音效共享配置的实例上限，满额时停止最早登记的实例并播放新音效。</summary>
        void PlaySfx(string fileName, SfxPlayOptions options = null);
        /// <inheritdoc cref="PlaySfx"/>
        UniTask PlaySfxAsync(string fileName, SfxPlayOptions options = null);
        /// <summary>播放 3D 音效，与同名 2D 音效共享配置的实例上限，满额时替换最早登记的实例。</summary>
        void PlaySfx3D(string fileName, Sfx3DPlayOptions options);
        /// <inheritdoc cref="PlaySfx3D"/>
        UniTask PlaySfx3DAsync(string fileName, Sfx3DPlayOptions options);
        /// <summary>停止所有已经登记的同名音效实例，包括等待延迟播放的实例。</summary>
        void StopSfx(string fileName);
        void StopSfx(AudioHandle handle);
        void StopSfxByGroup(string group);
        void StopAllSfx();
        void PlayClickSfx(string overrideSfx = null);
        string DefaultClickSfx { get; set; }

        #endregion

        #region Volume

        float MasterVolume { get; set; }
        float BGMVolume { get; set; }
        float SfxVolume { get; set; }

        #endregion

        #region Mute

        bool IsMasterMuted { get; set; }
        bool IsBGMMuted { get; set; }
        bool IsSfxMuted { get; set; }

        #endregion
    }
}
