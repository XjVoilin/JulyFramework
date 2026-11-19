using DG.Tweening;
using UnityEngine;

namespace July.Animation
{
    /// <summary>对象启用时，从 70% 放大到 120%，再回到原尺寸，播放一次。</summary>
    public sealed class ScaleIn : SimpleAnimationBase
    {
        [Header("开始大小")]
        [SerializeField] private float StartScaleMultiplier = 0.7f;
        
        [Header("结束大小")]
        [SerializeField] private float PeakScaleMultiplier = 1.1f;

        [Min(0.001f)]
        [Tooltip("放大的时长")]
        [SerializeField] private float _expandDuration = 0.1f;

        [Min(0.001f)]
        [Tooltip("缩小的时长")]
        [SerializeField] private float _settleDuration = 0.1f;

        [Min(0f)]
        [Tooltip("开始缩放前的等待时长")]
        [SerializeField] private float _delay;

        [Tooltip("延时期间隐藏，到时以起始倍率出现")]
        [SerializeField] private bool _hideDuringDelay;

        private Vector3? _restingScale;

        protected override Tween OnCreateTween()
        {
            var target = transform;
            var restingScale = target.localScale;
            _restingScale = restingScale;

            var startScale = restingScale * StartScaleMultiplier;
            var hideDuringDelay = _hideDuringDelay && _delay > 0f;
            target.localScale = hideDuringDelay ? Vector3.zero : startScale;

            var sequence = DOTween.Sequence()
                .SetTarget(target)
                .SetEase(Ease.Linear);

            if (hideDuringDelay)
            {
                sequence.AppendInterval(_delay)
                    .AppendCallback(() => target.localScale = startScale);
            }
            else
            {
                sequence.SetDelay(_delay, false);
            }

            return sequence
                .Append(target.DOScale(restingScale * PeakScaleMultiplier, _expandDuration).SetEase(Ease.OutCubic))
                .Append(target.DOScale(restingScale, _settleDuration).SetEase(Ease.OutCubic))
                .SetAutoKill(true)
                .SetRecyclable(false)
                .OnComplete(() => _restingScale = null);
        }

        protected override void OnReset()
        {
            if (_restingScale is not { } restingScale) return;

            transform.localScale = restingScale;
            _restingScale = null;
        }
    }
}
