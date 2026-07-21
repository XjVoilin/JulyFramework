using DG.Tweening;
using UnityEngine;

namespace July.Animation
{
    /// <summary>
    /// 上下浮动待机动画。
    /// 挂在 RectTransform 上即可，不指定 target 时默认为自身。
    /// </summary>
    public class UIFloatIdle : SimpleAnimationBase
    {
        [SerializeField] private RectTransform _target;

        [Tooltip("向上漂浮的振幅(像素)，元素会在基准位置和基准位置+振幅之间往返")]
        [SerializeField] private float _amplitude = 20f;

        [Tooltip("单次上浮/下沉时长(秒)，完整周期 = 2 * duration")]
        [SerializeField] private float _duration = 1f;

        [SerializeField] private Ease _ease = Ease.InOutSine;

        private Vector2 _basePos;

        protected override Tween OnCreateTween()
        {
            if (_target == null) _target = GetComponent<RectTransform>();
            if (_target == null) return null;

            _basePos = _target.anchoredPosition;
            return _target.DOAnchorPosY(_basePos.y + _amplitude, _duration)
                .SetEase(_ease)
                .SetLoops(-1, LoopType.Yoyo);
        }

        protected override void OnReset()
        {
            if (_target != null) _target.anchoredPosition = _basePos;
        }
    }
}
