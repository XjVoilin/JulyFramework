using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>
    /// 自动隐藏滚动条：滑动时从侧边滑入，停止滑动后延迟滑出隐藏。
    /// </summary>
    public class AutoHideScrollbar : MonoBehaviour
    {
        public enum Direction { Horizontal, Vertical }

        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform scrollbarRect;
        [SerializeField] private Direction direction = Direction.Horizontal;

        [Header("Timing")]
        [SerializeField] private float hideDelay = 2f;
        [SerializeField] private float slideDuration = 0.25f;

        [Header("Offset")]
        [Tooltip("隐藏时沿滚动条法线方向的偏移距离（正值：水平向右/垂直向下）")]
        [SerializeField] private float hideDistance = 70f;

        private Vector2 _showPos;
        private Vector2 _hidePos;
        private bool _isVisible;
        private bool _skipFirst;
        private Tween _slideTween;
        private Tween _timerTween;

        private void Start()
        {
            _showPos = scrollbarRect.anchoredPosition;

            var offset = direction == Direction.Horizontal
                ? new Vector2(hideDistance, 0f)
                : new Vector2(0f, -hideDistance);
            _hidePos = _showPos + offset;

            _isVisible = false;
            scrollbarRect.anchoredPosition = _hidePos;
        }

        private void OnEnable()
        {
            _skipFirst = true;
            scrollRect.onValueChanged.AddListener(OnScroll);
        }

        private void OnDisable()
        {
            scrollRect.onValueChanged.RemoveListener(OnScroll);
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
            _slideTween = scrollbarRect.DOAnchorPos(_showPos, slideDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        private void SlideOut()
        {
            KillSlide();
            _slideTween = scrollbarRect.DOAnchorPos(_hidePos, slideDuration)
                .SetEase(Ease.InCubic)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() => _isVisible = false);
        }

        private void ResetHideTimer()
        {
            KillTimer();
            _timerTween = DOVirtual.DelayedCall(hideDelay, SlideOut, false)
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
