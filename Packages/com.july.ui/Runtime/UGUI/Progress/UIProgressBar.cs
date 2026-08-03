using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    public enum UIProgressDirection
    {
        LeftToRight,
        RightToLeft,
        BottomToTop,
        TopToBottom
    }

    /// <summary>
    /// 基于 RectMask2D 实现的四向进度条，更新进度时不会缩放填充内容。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectMask2D))]
    public sealed class UIProgressBar : MonoBehaviour
    {
        [SerializeField] private RectMask2D _mask;
        [SerializeField] private UIProgressDirection _direction = UIProgressDirection.LeftToRight;

        private Vector4 _basePadding;
        private float _normalizedValue;
        private bool _initialized;

        public float NormalizedValue => _normalizedValue;

        /// <summary>
        /// 更新当前进度；当最大值小于或等于零时，按空进度处理。
        /// </summary>
        public void SetValue(float current, float maximum)
        {
            var normalizedValue = maximum > 0f ? current / maximum : 0f;
            _normalizedValue = Sanitize(normalizedValue);

            EnsureInitialized();
            ApplyValue();
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Reset()
        {
            _mask = GetComponent<RectMask2D>();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_initialized)
                ApplyValue();
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            if (_mask == null)
                _mask = GetComponent<RectMask2D>();

            _basePadding = _mask.padding;
            _initialized = true;
        }

        private void ApplyValue()
        {
            var hiddenRatio = 1f - _normalizedValue;
            var padding = _basePadding;

            switch (_direction)
            {
                case UIProgressDirection.RightToLeft:
                    padding.x += GetAvailableWidth() * hiddenRatio;
                    break;
                case UIProgressDirection.BottomToTop:
                    padding.w += GetAvailableHeight() * hiddenRatio;
                    break;
                case UIProgressDirection.TopToBottom:
                    padding.y += GetAvailableHeight() * hiddenRatio;
                    break;
                default:
                    padding.z += GetAvailableWidth() * hiddenRatio;
                    break;
            }

            _mask.padding = padding;
        }

        private float GetAvailableWidth()
        {
            return Mathf.Max(
                0f,
                _mask.rectTransform.rect.width - _basePadding.x - _basePadding.z);
        }

        private float GetAvailableHeight()
        {
            return Mathf.Max(
                0f,
                _mask.rectTransform.rect.height - _basePadding.y - _basePadding.w);
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsNegativeInfinity(value))
                return 0f;
            if (float.IsPositiveInfinity(value))
                return 1f;
            return Mathf.Clamp01(value);
        }
    }
}
