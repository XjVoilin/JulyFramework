using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using July.Arch;
using July.Resource;
using UnityEngine;

namespace July.Audio
{
    /// <summary>
    /// 音频系统 — 合并原 AudioModule（业务层）+ UnityAudioProvider（技术层）。
    /// 通过 ArchContext.GetSystem&lt;AudioSystem&gt;() 获取。
    /// </summary>
    public class AudioSystem : SystemBase, IAudioSystem, IUpdatableSystem
    {
        #region Fields

        private IResourceSystem _resourceSystem;

        // AudioSource 池
        private readonly Stack<AudioSource> _pool = new();
        private const int PoolInitialSize = 5;
        private const int PoolMaxSize = 20;
        private int _poolCreatedCount;

        // 活跃 Handle 追踪
        private readonly HashSet<AudioHandle> _activeHandles = new();
        private readonly List<AudioHandle> _invalidHandleList = new();

        // DOTween fade
        private readonly Dictionary<AudioHandle, Tweener> _fadeTweeners = new();

        // BGM 业务状态
        private AudioHandle _currentBgmHandle;
        private float _bgmVolume = 1f;
        private bool _bgmMute;

        // SFX 业务状态
        private readonly Dictionary<AudioHandle, SfxInfo> _activeSfxHandles = new();
        private readonly Dictionary<string, AudioHandle> _nameToSfxHandle = new();
        private float _sfxVolume = 1f;
        private bool _sfxMute;

        // 主音量
        private float _masterVolume = 1f;
        private bool _masterMute;

        private class SfxInfo
        {
            public string Group;
            public float BaseVolume;
        }

        #endregion

        #region Lifecycle

        protected override UniTask OnInitializeAsync()
        {
            _resourceSystem = GetSystem<IResourceSystem>();
            PrewarmPool();
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown()
        {
            StopBGM(0f);
            StopAllSfx();

            // 清理池
            while (_pool.Count > 0)
            {
                var src = _pool.Pop();
                if (src != null && src.gameObject != null)
                    UnityEngine.Object.Destroy(src.gameObject);
            }

            _activeHandles.Clear();
            _activeSfxHandles.Clear();
            _nameToSfxHandle.Clear();
            _fadeTweeners.Clear();
            _invalidHandleList.Clear();
            _currentBgmHandle = null;
            _resourceSystem = null;
        }

        public void OnUpdate(float deltaTime)
        {
            // 清理无效 BGM 句柄
            if (_currentBgmHandle != null && !_currentBgmHandle.IsValid)
                _currentBgmHandle = null;

            // 清理无效 SFX 句柄（Module 层）
            foreach (var kvp in _activeSfxHandles)
            {
                if (kvp.Key == null || !kvp.Key.IsValid)
                    _invalidHandleList.Add(kvp.Key);
            }

            if (_invalidHandleList.Count > 0)
            {
                foreach (var h in _invalidHandleList)
                    _activeSfxHandles.Remove(h);
                _invalidHandleList.Clear();
            }

            // Provider 层：检测播放完成的 Handle
            foreach (var handle in _activeHandles)
            {
                if (handle == null || !handle.IsValid || handle.AudioSource == null)
                {
                    _invalidHandleList.Add(handle);
                    continue;
                }

                if (Time.realtimeSinceStartup >= handle.ExpectedEndTime)
                {
                    if (!handle.IsPaused && !handle.AudioSource.loop && !handle.AudioSource.isPlaying)
                        _invalidHandleList.Add(handle);
                }
            }

            if (_invalidHandleList.Count > 0)
            {
                foreach (var handle in _invalidHandleList)
                    CleanupHandle(handle);
                _invalidHandleList.Clear();
            }
        }

        #endregion

        #region BGM

        public async UniTask<bool> PlayBGMAsync(string fileName, BGMPlayOptions options = null,
            CancellationToken ct = default)
        {
            if (_currentBgmHandle != null)
            {
                var fadeOut = options?.FadeOutDuration ?? 0f;
                StopAudioInternal(_currentBgmHandle, fadeOut);
                _currentBgmHandle = null;
            }

            var techOptions = new AudioPlayOptions
            {
                Loop = options?.Loop ?? true,
                Volume = CalculateBGMVolume(options?.Volume),
                FadeInDuration = options?.FadeInDuration ?? 0f
            };

            var handle = await PlayAudioInternalAsync(fileName, techOptions, ct);
            if (handle == null) return false;

            _currentBgmHandle = handle;
            ApplyMuteToBGM();
            return true;
        }

        public void PlayBGM(string fileName, BGMPlayOptions options = null)
        {
            PlayBGMAsync(fileName, options).Forget();
        }

        public void StopBGM(float fadeOutDuration = 0f)
        {
            if (_currentBgmHandle == null) return;
            StopAudioInternal(_currentBgmHandle, fadeOutDuration);
            _currentBgmHandle = null;
        }

        public void PauseBGM()
        {
            if (_currentBgmHandle == null) return;
            PauseAudioInternal(_currentBgmHandle);
        }

        public void ResumeBGM()
        {
            if (_currentBgmHandle == null) return;
            ResumeAudioInternal(_currentBgmHandle);
        }

        public bool IsBGMPlaying()
        {
            if (_currentBgmHandle == null) return false;
            return IsPlayingInternal(_currentBgmHandle);
        }

        public AudioHandle GetCurrentBGMHandle() => _currentBgmHandle;

        #endregion

        #region SFX

        public async UniTask PlaySfxAsync(string fileName, SfxPlayOptions options = null)
        {
            var techOptions = new AudioPlayOptions
            {
                Volume = CalculateSfxVolume(options?.Volume),
                Priority = options?.Priority ?? 128,
                Pitch = options?.Pitch ?? 1f,
                Delay = options?.Delay ?? 0f,
                Loop = false
            };

            var handle = await PlayAudioInternalAsync(fileName, techOptions);
            if (handle == null) return;

            RegisterSfxHandle(fileName, handle, options?.Group, techOptions.Volume);
        }

        public void PlaySfx(string fileName, SfxPlayOptions options = null)
        {
            PlaySfxAsync(fileName, options).Forget();
        }

        public async UniTask PlaySfx3DAsync(string fileName, Sfx3DPlayOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var techOptions = new AudioPlayOptions
            {
                Volume = CalculateSfxVolume(options.Volume),
                Priority = options.Priority,
                Pitch = options.Pitch,
                Delay = options.Delay,
                Loop = false,
                Position = new AudioPosition3D(options.Position.X, options.Position.Y, options.Position.Z),
                MinDistance = options.MinDistance,
                MaxDistance = options.MaxDistance
            };

            var handle = await PlayAudioInternalAsync(fileName, techOptions);
            if (handle == null) return;

            RegisterSfxHandle(fileName, handle, options.Group, techOptions.Volume);
        }

        public void PlaySfx3D(string fileName, Sfx3DPlayOptions options)
        {
            PlaySfx3DAsync(fileName, options).Forget();
        }

        public string DefaultClickSfx { get; set; } = AudioConfig.Default.DefaultClickSfx;

        public void Configure(AudioConfig config)
        {
            DefaultClickSfx = config.DefaultClickSfx;
        }

        public void PlayClickSfx(string overrideSfx = null)
        {
            var sfx = string.IsNullOrEmpty(overrideSfx) ? DefaultClickSfx : overrideSfx;
            if (string.IsNullOrEmpty(sfx)) return;
            PlaySfxAsync(sfx).Forget();
        }

        private void RegisterSfxHandle(string fileName, AudioHandle handle, string group, float baseVolume)
        {
            if (_nameToSfxHandle.TryGetValue(fileName, out var prev) && prev.IsValid)
            {
                StopAudioInternal(prev);
                _activeSfxHandles.Remove(prev);
            }

            _nameToSfxHandle[fileName] = handle;
            _activeSfxHandles[handle] = new SfxInfo { Group = group, BaseVolume = baseVolume };

            if (_masterMute || _sfxMute)
                SetMuteInternal(handle, true);
        }

        public void StopSfx(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            if (!_nameToSfxHandle.TryGetValue(fileName, out var handle)) return;

            _nameToSfxHandle.Remove(fileName);
            if (handle != null && handle.IsValid)
            {
                StopAudioInternal(handle);
                _activeSfxHandles.Remove(handle);
            }
        }

        public void StopSfx(AudioHandle handle)
        {
            if (handle == null || !handle.IsValid) return;

            if (_activeSfxHandles.ContainsKey(handle))
            {
                StopAudioInternal(handle);
                _activeSfxHandles.Remove(handle);

                if (!string.IsNullOrEmpty(handle.AudioIdentifier))
                {
                    if (_nameToSfxHandle.TryGetValue(handle.AudioIdentifier, out var mapped) && mapped == handle)
                        _nameToSfxHandle.Remove(handle.AudioIdentifier);
                }
            }
        }

        public void StopAllSfx()
        {
            var handles = new List<AudioHandle>(_activeSfxHandles.Keys);
            foreach (var handle in handles)
            {
                if (handle != null && handle.IsValid)
                    StopAudioInternal(handle);
            }

            _activeSfxHandles.Clear();
            _nameToSfxHandle.Clear();
        }

        public void StopSfxByGroup(string group)
        {
            if (string.IsNullOrEmpty(group)) return;

            var toStop = new List<AudioHandle>();
            foreach (var kvp in _activeSfxHandles)
            {
                if (kvp.Value.Group == group && kvp.Key != null && kvp.Key.IsValid)
                    toStop.Add(kvp.Key);
            }

            foreach (var handle in toStop)
            {
                StopAudioInternal(handle);
                _activeSfxHandles.Remove(handle);
            }
        }

        #endregion

        #region Volume & Mute

        public float MasterVolume
        {
            get => _masterVolume;
            set { _masterVolume = Mathf.Clamp01(value); ApplyVolumeToAll(); }
        }

        public float BGMVolume
        {
            get => _bgmVolume;
            set { _bgmVolume = Mathf.Clamp01(value); ApplyVolumeToBGM(); }
        }

        public float SfxVolume
        {
            get => _sfxVolume;
            set { _sfxVolume = Mathf.Clamp01(value); ApplyVolumeToSFX(); }
        }

        public bool IsMasterMuted
        {
            get => _masterMute;
            set { _masterMute = value; ApplyMuteToAll(); }
        }

        public bool IsBGMMuted
        {
            get => _bgmMute;
            set { _bgmMute = value; ApplyMuteToBGM(); }
        }

        public bool IsSfxMuted
        {
            get => _sfxMute;
            set { _sfxMute = value; ApplyMuteToSFX(); }
        }

        #endregion

        #region Internal - Audio Playback (from UnityAudioProvider)

        private async UniTask<AudioHandle> PlayAudioInternalAsync(string fileName, AudioPlayOptions options,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(fileName)) return null;

            var clipHandle = await _resourceSystem.LoadAssetAsync<AudioClip>(fileName, ct);
            if (clipHandle?.Asset == null)
            {
                clipHandle?.Dispose();
                return null;
            }

            var audioClip = clipHandle.Asset;

            try
            {
                var audioSource = GetFromPool();
                options ??= new AudioPlayOptions();
                ApplyOptionsToAudioSource(audioSource, options);
                audioSource.clip = audioClip;

                var handle = new AudioHandle
                {
                    AudioClip = audioClip,
                    ResourceHandle = clipHandle,
                    AudioIdentifier = fileName,
                    AudioSource = audioSource,
                    Priority = options.Priority,
                    BaseVolume = options.Volume,
                    ExpectedEndTime = CalculateExpectedEndTime(audioClip, options),
                    IsPaused = false
                };

                _activeHandles.Add(handle);

                if (options.Delay > 0f)
                    audioSource.PlayDelayed(options.Delay);
                else
                    audioSource.Play();

                if (options.FadeInDuration > 0f)
                {
                    audioSource.volume = 0f;
                    StartFadeIn(handle, options.FadeInDuration, options.Delay);
                }

                return handle;
            }
            catch (Exception)
            {
                clipHandle.Dispose();
                return null;
            }
        }

        private void StopAudioInternal(AudioHandle handle, float fadeOutDuration = 0f)
        {
            if (handle == null || !handle.IsValid || !_activeHandles.Contains(handle)) return;

            StopFadeTween(handle);

            if (fadeOutDuration > 0f)
                StartFadeOut(handle, fadeOutDuration, () => ActuallyStopAudio(handle));
            else
                ActuallyStopAudio(handle);
        }

        private void PauseAudioInternal(AudioHandle handle)
        {
            if (handle == null || !handle.IsValid || !_activeHandles.Contains(handle) || handle.IsPaused) return;
            handle.AudioSource.Pause();
            handle.IsPaused = true;
        }

        private void ResumeAudioInternal(AudioHandle handle)
        {
            if (handle == null || !handle.IsValid || !_activeHandles.Contains(handle) || !handle.IsPaused) return;
            handle.AudioSource.UnPause();
            handle.IsPaused = false;
        }

        private void SetVolumeInternal(AudioHandle handle, float volume)
        {
            if (handle == null || !handle.IsValid || !_activeHandles.Contains(handle)) return;
            handle.BaseVolume = Mathf.Clamp01(volume);
            handle.AudioSource.volume = handle.BaseVolume;
        }

        private void SetMuteInternal(AudioHandle handle, bool mute)
        {
            if (handle == null || !handle.IsValid || !_activeHandles.Contains(handle)) return;
            handle.AudioSource.mute = mute;
        }

        private bool IsPlayingInternal(AudioHandle handle)
        {
            if (handle == null || !handle.IsValid || !_activeHandles.Contains(handle) || handle.IsPaused) return false;
            return handle.AudioSource.isPlaying;
        }

        private void ActuallyStopAudio(AudioHandle handle)
        {
            if (!_activeHandles.Contains(handle)) return;
            if (handle.AudioSource != null) handle.AudioSource.Stop();
            CleanupHandle(handle);
        }

        private void CleanupHandle(AudioHandle handle)
        {
            if (handle == null) return;

            handle.IsValid = false;
            StopFadeTween(handle);

            if (_activeHandles.Contains(handle))
            {
                if (handle.AudioSource != null) ReturnToPool(handle.AudioSource);
                _activeHandles.Remove(handle);
            }

            handle.ResourceHandle?.Dispose();
        }

        #endregion

        #region Internal - AudioSource Pool

        private void PrewarmPool()
        {
            for (int i = 0; i < PoolInitialSize; i++)
                _pool.Push(CreateAudioSource());
        }

        private AudioSource GetFromPool()
        {
            AudioSource src;
            if (_pool.Count > 0)
            {
                src = _pool.Pop();
            }
            else
            {
                src = CreateAudioSource();
            }

            src.gameObject.SetActive(true);
            return src;
        }

        private void ReturnToPool(AudioSource src)
        {
            if (src == null) return;

            src.Stop();
            src.clip = null;
            src.volume = 1f;
            src.pitch = 1f;
            src.mute = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.gameObject.SetActive(false);

            if (_pool.Count < PoolMaxSize)
                _pool.Push(src);
            else
                UnityEngine.Object.Destroy(src.gameObject);
        }

        private AudioSource CreateAudioSource()
        {
            _poolCreatedCount++;
            var go = new GameObject($"AudioSource_{_poolCreatedCount}");
            go.transform.SetParent(null);
            UnityEngine.Object.DontDestroyOnLoad(go);

            var audioSource = go.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            go.SetActive(false);

            return audioSource;
        }

        #endregion

        #region Internal - Volume Helpers

        private float CalculateBGMVolume(float? volume)
        {
            var baseVol = volume ?? _bgmVolume;
            return baseVol * _masterVolume;
        }

        private float CalculateSfxVolume(float? volume)
        {
            var baseVol = volume ?? _sfxVolume;
            return baseVol * _masterVolume;
        }

        private void ApplyVolumeToAll()
        {
            ApplyVolumeToBGM();
            ApplyVolumeToSFX();
        }

        private void ApplyVolumeToBGM()
        {
            if (_currentBgmHandle == null) return;
            SetVolumeInternal(_currentBgmHandle, _bgmVolume * _masterVolume);
        }

        private void ApplyVolumeToSFX()
        {
            var finalVol = _sfxVolume * _masterVolume;
            foreach (var kvp in _activeSfxHandles)
            {
                if (kvp.Key != null && kvp.Key.IsValid)
                    SetVolumeInternal(kvp.Key, kvp.Value.BaseVolume * finalVol);
            }
        }

        private void ApplyMuteToAll()
        {
            ApplyMuteToBGM();
            ApplyMuteToSFX();
        }

        private void ApplyMuteToBGM()
        {
            if (_currentBgmHandle == null) return;
            SetMuteInternal(_currentBgmHandle, _masterMute || _bgmMute);
        }

        private void ApplyMuteToSFX()
        {
            bool mute = _masterMute || _sfxMute;
            foreach (var kvp in _activeSfxHandles)
            {
                if (kvp.Key != null && kvp.Key.IsValid)
                    SetMuteInternal(kvp.Key, mute);
            }
        }

        #endregion

        #region Internal - DOTween Fade

        private void StartFadeIn(AudioHandle handle, float duration, float delay = 0f)
        {
            if (!_activeHandles.Contains(handle)) return;

            StopFadeTween(handle);
            var target = handle.BaseVolume;
            handle.AudioSource.volume = 0f;

            var tweener = handle.AudioSource.DOFade(target, duration);
            if (delay > 0f) tweener = tweener.SetDelay(delay);

            tweener.SetEase(Ease.Linear)
                .OnComplete(() => _fadeTweeners.Remove(handle));

            _fadeTweeners[handle] = tweener;
        }

        private void StartFadeOut(AudioHandle handle, float duration, Action onComplete)
        {
            if (!_activeHandles.Contains(handle))
            {
                onComplete?.Invoke();
                return;
            }

            var tweener = handle.AudioSource.DOFade(0f, duration)
                .SetEase(Ease.Linear)
                .OnComplete(() =>
                {
                    _fadeTweeners.Remove(handle);
                    onComplete?.Invoke();
                });

            _fadeTweeners[handle] = tweener;
        }

        private void StopFadeTween(AudioHandle handle)
        {
            if (_fadeTweeners.TryGetValue(handle, out var tweener))
            {
                tweener?.Kill();
                _fadeTweeners.Remove(handle);
            }
        }

        #endregion

        #region Internal - Helpers

        private void ApplyOptionsToAudioSource(AudioSource audioSource, AudioPlayOptions options)
        {
            audioSource.volume = options.Volume;
            audioSource.loop = options.Loop;
            audioSource.priority = options.Priority;
            audioSource.pitch = options.Pitch;

            if (options.Position != AudioPosition3D.Zero)
            {
                audioSource.spatialBlend = 1f;
                audioSource.transform.position = new Vector3(options.Position.X, options.Position.Y, options.Position.Z);
                audioSource.minDistance = options.MinDistance;
                audioSource.maxDistance = options.MaxDistance;
            }
            else
            {
                audioSource.spatialBlend = 0f;
            }
        }

        private float CalculateExpectedEndTime(AudioClip clip, AudioPlayOptions options)
        {
            if (clip == null) return 0f;
            var duration = clip.length / Mathf.Max(options.Pitch, 0.01f);
            return Time.realtimeSinceStartup + options.Delay + duration;
        }

        #endregion
    }
}
