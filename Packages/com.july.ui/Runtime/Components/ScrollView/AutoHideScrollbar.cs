using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>
    /// 自动隐藏滚动条：滑动时从侧边滑入，停止滑动后延迟滑出隐藏。
    /// </summary>
    public class AutoHideScrollbar : MonoBehaviour
    {
        public enum Direction { Horizontal, Vertical }

        [FormerlySerializedAs("scrollRect")]
        [SerializeField] private ScrollRect _scrollRect;
        [FormerlySerializedAs("scrollbarRect")]
        [SerializeField] private RectTransform _scrollbarRect;
        [FormerlySerializedAs("direction")]
        [SerializeField] private Direction _direction = Direction.Horizontal;

        [Header("Timing")]
        [FormerlySerializedAs("hideDelay")]
        [SerializeField] private float _hideDelay = 2f;
        [FormerlySerializedAs("slideDuration")]
        [SerializeField] private float _slideDuration = 0.25f;

        [Header("Offset")]
        [Tooltip("隐藏时沿滚动条法线方向的偏移距离（正值：水平向右/垂直向下）")]
        [FormerlySerializedAs("hideDistance")]
        [SerializeField] private float _hideDistance = 70f;

        private Vector2 _showPos;
        private Vector2 _hidePos;
        private bool _isVisible;
        private bool _skipFirst;
        private Tween _slideTween;
        private Tween _timerTween;

        private void Start()
        {
            _showPos = _scrollbarRect.anchoredPosition;

            var offset = _direction == Direction.Horizontal
                ? new Vector2(_hideDistance, 0f)
                : new Vector2(0f, -_hideDistance);
            _hidePos = _showPos + offset;

            _isVisible = false;
            _scrollbarRect.anchoredPosition = _hidePos;
        }

        private void OnEnable()
        {
            _skipFirst = true;
            _scrollRect.onValueChanged.AddListener(OnScroll);
        }

        private void OnDisable()
        {
            _scrollRect.onValueChanged.RemoveListener(OnScroll);
            KillAll();
        }

        private void OnDestroy()
        {
            KillAll();
        }

        private void OnScroll(Vector2 _)
        {
            if (_skipFirst)
            {
                _skipFirst = false;
                return;
            }

            if (!_isVisible)
            {
                _isVisible = true;
                SlideIn();
            }

            ResetHideTimer();
        }

        private void SlideIn()
        {
            KillSlide();
            _slideTween = _scrollbarRect.DOAnchorPos(_showPos, _slideDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        private void SlideOut()
        {
            KillSlide();
            _slideTween = _scrollbarRect.DOAnchorPos(_hidePos, _slideDuration)
                .SetEase(Ease.InCubic)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() => _isVisible = false);
        }

        private void ResetHideTimer()
        {
            KillTimer();
            _timerTween = DOVirtual.DelayedCall(_hideDelay, SlideOut, false)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        private void KillSlide()
        {
            if (_slideTween != null && _slideTween.IsActive()) _slideTween.Kill();
            _slideTween = null;
        }

        private void KillTimer()
        {
            if (_timerTween != null && _timerTween.IsActive()) _timerTween.Kill();
            _timerTween = null;
        }

        private void KillAll()
        {
            KillSlide();
            KillTimer();
        }
    }
}
