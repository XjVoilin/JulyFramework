using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace July.Animation
{
    /// <summary>
    /// 通用 Sprite 序列帧动画播放器。
    /// 自动检测 SpriteRenderer 或 Image，适用于世界场景和 UI。
    /// 支持多片段（Clip）、循环/单次播放。
    /// 在 Inspector 中将 Sprite 拖入 clips → frames 即可，配合 Prefab Variant 实现换皮。
    /// </summary>
    [DisallowMultipleComponent]
    public class SpriteFrameAnimator : MonoBehaviour, ISimpleAnimation
    {
        [Serializable]
        public class Clip
        {
            [Tooltip("片段名称，用于 Play(name) 调用")]
            public string name;

            [Tooltip("帧序列")]
            public Sprite[] frames = Array.Empty<Sprite>();

            [Tooltip("播放帧率"), Min(0.1f)]
            public float fps = 10f;

            [Tooltip("是否循环播放")]
            public bool loop = true;

            public bool IsValid => frames != null && frames.Length > 0;
        }

        [SerializeField] private List<Clip> _clips = new();

        [Header("播放设置")]
        [Tooltip("OnEnable 时自动播放")]
        [SerializeField] private bool _playOnEnable = true;

        [Tooltip("自动播放的片段名（留空则播放第一个）")]
        [SerializeField] private string _defaultClip;

        [Tooltip("使用 unscaledDeltaTime（暂停/TimeScale=0 时仍播放）")]
        [SerializeField] private bool _ignoreTimeScale;

        private Action<Sprite> _applySpriteFunc;
        private Clip _current;
        private int _frameIndex;
        private float _timer;
        private bool _playing;
        private bool _paused;

        /// <summary>非循环片段播完时触发，参数为片段名。</summary>
        public event Action<string> OnClipComplete;

        #region Properties

        public bool IsPlaying => _playing && !_paused;
        public string CurrentClipName => _current?.name;
        public int CurrentFrame => _frameIndex;
        public int ClipCount => _clips.Count;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            ResolveRenderer();
        }

        private void OnEnable()
        {
            if (!_playOnEnable || _clips.Count == 0) return;
            var target = !string.IsNullOrEmpty(_defaultClip) ? _defaultClip : _clips[0].name;
            Play(target);
        }

        private void OnDisable()
        {
            _playing = false;
            _paused = false;
        }

        private void Update()
        {
            if (!_playing || _paused) return;

            var clip = _current;
            if (clip == null || !clip.IsValid) return;

            _timer += _ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

            var interval = 1f / clip.fps;
            if (_timer < interval) return;

            _timer -= interval;
            _frameIndex++;

            if (_frameIndex >= clip.frames.Length)
            {
                if (clip.loop)
                {
                    _frameIndex = 0;
                }
                else
                {
                    _frameIndex = clip.frames.Length - 1;
                    _playing = false;
                    OnClipComplete?.Invoke(clip.name);
                    return;
                }
            }

            _applySpriteFunc?.Invoke(clip.frames[_frameIndex]);
        }

        #endregion

        #region ISimpleAnimation

        void ISimpleAnimation.Play()
        {
            if (_current != null)
                Play(_current.name);
            else if (_clips.Count > 0)
                Play(!string.IsNullOrEmpty(_defaultClip) ? _defaultClip : _clips[0].name);
        }

        public void Stop()
        {
            _playing = false;
            _paused = false;
            _frameIndex = 0;
            _timer = 0f;
        }

        #endregion

        #region Public API — 播放控制

        /// <summary>从头播放指定名称的片段。</summary>
        public void Play(string clipName)
        {
            var clip = FindClip(clipName);
            if (clip == null || !clip.IsValid)
            {
                Debug.LogWarning($"[SpriteFrameAnimator] Clip '{clipName}' not found or empty on {name}");
                return;
            }

            if (_applySpriteFunc == null) ResolveRenderer();

            _current = clip;
            _frameIndex = 0;
            _timer = 0f;
            _playing = true;
            _paused = false;
            _applySpriteFunc?.Invoke(clip.frames[0]);
        }

        /// <summary>从指定帧开始播放。</summary>
        public void Play(string clipName, int startFrame)
        {
            var clip = FindClip(clipName);
            if (clip == null || !clip.IsValid) return;

            if (_applySpriteFunc == null) ResolveRenderer();

            _current = clip;
            _frameIndex = Mathf.Clamp(startFrame, 0, clip.frames.Length - 1);
            _timer = 0f;
            _playing = true;
            _paused = false;
            _applySpriteFunc?.Invoke(clip.frames[_frameIndex]);
        }

        public void Pause() => _paused = true;

        public void Resume() => _paused = false;

        /// <summary>跳转到指定帧（不改变播放状态）。</summary>
        public void SetFrame(int frame)
        {
            if (_current == null || !_current.IsValid) return;
            _frameIndex = Mathf.Clamp(frame, 0, _current.frames.Length - 1);
            _applySpriteFunc?.Invoke(_current.frames[_frameIndex]);
        }

        public bool HasClip(string clipName) => FindClip(clipName) != null;

        #endregion

        #region Internal

        private void ResolveRenderer()
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                _applySpriteFunc = s => sr.sprite = s;
                return;
            }

            var img = GetComponent<Image>();
            if (img != null)
            {
                _applySpriteFunc = s => img.sprite = s;
                return;
            }

            Debug.LogWarning($"[SpriteFrameAnimator] No SpriteRenderer or Image on {name}");
        }

        private Clip FindClip(string clipName)
        {
            for (int i = 0; i < _clips.Count; i++)
                if (_clips[i].name == clipName)
                    return _clips[i];
            return null;
        }

        #endregion
    }
}
