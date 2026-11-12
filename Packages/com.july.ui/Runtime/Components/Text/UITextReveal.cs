using TMPro;
using UnityEngine;

namespace July.UI
{
    /// <summary>逐步显示整段或末尾 TMP 字符，保持完整排版；停止时恢复原始显示设置。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("July/UI/Text Reveal")]
    public sealed class UITextReveal : MonoBehaviour
    {
        [Tooltip("0 表示整段文本；大于 0 时末尾这些字符从全部隐藏逐步显示。")]
        [Min(0)] [SerializeField] private int _animatedCharacterCount;
        [Min(0.001f)] [SerializeField] private float _characterInterval = 0.05f;
        [Tooltip("总播放次数：1 为播放一次，正整数为指定次数，-1 为无限循环。 ")]
        [Min(-1)] [SerializeField] private int _loopCount = 1;
        [SerializeField] private bool _ignoreTimeScale = true;
        [SerializeField] private bool _playOnEnable = true;

        private TMP_Text _text;
        private float _elapsed;
        private int _fixedCharacterCount;
        private int _animatedCharacters;
        private bool _isPlaying;
        private int _originalVisibleCharacters;
        private bool _originalUseMaxVisibleDescender;
        private bool _pendingPlay;
        private bool _ownsVisibility;

        private void Awake() => _text = GetComponent<TMP_Text>();

        private void OnEnable()
        {
            if (_playOnEnable)
                Play();
        }

        public void Play()
        {
            _isPlaying = false;
            _pendingPlay = true;
        }

        public void Stop()
        {
            _pendingPlay = false;
            _isPlaying = false;
            if (!_ownsVisibility)
                return;
            _ownsVisibility = false;
            _text.maxVisibleCharacters = _originalVisibleCharacters;
            _text.useMaxVisibleDescender = _originalUseMaxVisibleDescender;
        }

        private void OnDisable() => Stop();

        private void LateUpdate()
        {
            if (_pendingPlay)
            {
                BeginPlayback();
                return;
            }
            if (!_isPlaying)
                return;

            _elapsed += _ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
            var step = Mathf.FloorToInt(_elapsed / _characterInterval);
            var cycleLength = _animatedCharacters + 1;
            if (_loopCount != -1 && step >= _loopCount * cycleLength - 1)
            {
                _text.maxVisibleCharacters = _fixedCharacterCount + _animatedCharacters;
                _isPlaying = false;
                return;
            }
            _text.maxVisibleCharacters = _fixedCharacterCount + step % cycleLength;
        }

        private void BeginPlayback()
        {
            _pendingPlay = false;
            if (!_ownsVisibility)
            {
                _originalVisibleCharacters = _text.maxVisibleCharacters;
                _originalUseMaxVisibleDescender = _text.useMaxVisibleDescender;
                _ownsVisibility = true;
            }

            // 等本帧 OnEnable（包括本地化赋值）结束后再解析完整文本。
            _text.useMaxVisibleDescender = false;
            _text.ForceMeshUpdate();
            var total = _text.textInfo.characterCount;
            _animatedCharacters = _animatedCharacterCount == 0 ? total : Mathf.Min(_animatedCharacterCount, total);
            _fixedCharacterCount = total - _animatedCharacters;
            _text.maxVisibleCharacters = _fixedCharacterCount;
            _elapsed = 0f;
            _isPlaying = _animatedCharacters > 0;
        }
    }
}
