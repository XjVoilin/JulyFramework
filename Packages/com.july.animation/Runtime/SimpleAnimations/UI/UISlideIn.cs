using DG.Tweening;
using UnityEngine;

namespace July.Animation
{
    /// <summary>UI 节点启用时，从垂直偏移位置滑入当前锚点位置。</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class UISlideIn : SimpleAnimationBase
    {
        [SerializeField] private float _verticalOffset = 180f;
        [SerializeField] private float _duration = 0.3f;
        [SerializeField] private float _strength = 3f;

        private RectTransform _rectTransform;
        private Vector2 _restingPosition;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _restingPosition = _rectTransform.anchoredPosition;
        }

        protected override Tween OnCreateTween()
        {
            _rectTransform.anchoredPosition =
                _restingPosition + Vector2.down * _verticalOffset;

            return _rectTransform
                .DOAnchorPos(_restingPosition, _duration)
                .SetEase(Ease.OutBack, _strength);
        }

        protected override void OnReset()
        {
            _rectTransform.anchoredPosition = _restingPosition;
        }
    }
}
