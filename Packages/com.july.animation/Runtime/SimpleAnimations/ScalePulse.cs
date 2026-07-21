using DG.Tweening;
using UnityEngine;

namespace July.Animation
{
    /// <summary>
    /// 缩放脉冲动画：放大 → 缩回 → 等待 → 循环。
    /// 适用于任意 Transform（UI 红点、3D 道具高亮等）。
    /// </summary>
    public class ScalePulse : SimpleAnimationBase
    {
        [SerializeField] private Transform _target;

        [Tooltip("目标缩放倍率（相对于初始 Scale）")]
        [SerializeField] private float _scaleTo = 1.2f;

        [Tooltip("单次放大时长(秒)")]
        [SerializeField] private float _punchDuration = 0.25f;

        [Tooltip("放大后停留时长(秒)，0 表示不停留")]
        [SerializeField] private float _holdDuration;

        [Tooltip("单次缩回时长(秒)")]
        [SerializeField] private float _returnDuration = 0.2f;

        [Tooltip("两次脉冲之间的间隔(秒)，0 表示连续")]
        [SerializeField] private float _interval = 2.4f;

        [SerializeField] private Ease _punchEase = Ease.OutQuad;
        [SerializeField] private Ease _returnEase = Ease.InQuad;

        private Vector3 _baseScale;

        protected override Tween OnCreateTween()
        {
            if (_target == null) _target = transform;
            _baseScale = _target.localScale;

            var peak = _baseScale * _scaleTo;
            var seq = DOTween.Sequence()
                .Append(_target.DOScale(peak, _punchDuration).SetEase(_punchEase));

            if (_holdDuration > 0f)
                seq.AppendInterval(_holdDuration);

            seq.Append(_target.DOScale(_baseScale, _returnDuration).SetEase(_returnEase));

            if (_interval > 0f)
                seq.AppendInterval(_interval);

            return seq.SetLoops(-1);
        }

        protected override void OnReset()
        {
            if (_target != null) _target.localScale = _baseScale;
        }
    }
}
