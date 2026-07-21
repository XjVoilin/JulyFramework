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

        void PlaySfx(string fileName, SfxPlayOptions options = null);
        UniTask PlaySfxAsync(string fileName, SfxPlayOptions options = null);
        void PlaySfx3D(string fileName, Sfx3DPlayOptions options);
        UniTask PlaySfx3DAsync(string fileName, Sfx3DPlayOptions options);
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
