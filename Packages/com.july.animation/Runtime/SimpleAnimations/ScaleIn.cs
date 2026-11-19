using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

namespace July.Animation
{
    /// <summary>对象启用时，使用 OutBack 缓动从起始倍率缩放到原尺寸。</summary>
    public sealed class ScaleIn : SimpleAnimationBase
    {
        [FormerlySerializedAs("StartScaleMultiplier")]
        [Min(0f)]
        [Tooltip("起始倍率，相对于播放前的缩放")]
        [SerializeField] private float _startScaleMultiplier = 0.7f;

        [Min(0.001f)]
        [Tooltip("动画时长（秒），不含延时")]
        [SerializeField] private float _duration = 0.3f;

        [Min(0f)]
        [Tooltip("OutBack 的回弹强度。填 0 时直接收敛到原尺寸。")]
        [SerializeField] private float _overshoot = 3f;

        [Min(0f)]
        [Tooltip("开始缩放前的等待时长（秒）")]
        [SerializeField] private float _delay;

        [Tooltip("延时期间隐藏，到时以起始倍率出现")]
        [SerializeField] private bool _hideDuringDelay;

        private Vector3? _restingScale;

        protected override Tween OnCreateTween()
        {
            var target = transform;
            var restingScale = target.localScale;
            _restingScale = restingScale;

            var startScale = restingScale * _startScaleMultiplier;
            var hideDuringDelay = _hideDuringDelay && _delay > 0f;
            target.localScale = hideDuringDelay ? Vector3.zero : startScale;

            var scaleTween = target.DOScale(restingScale, _duration)
                .SetEase(Ease.OutBack, _overshoot);

            Tween playback = hideDuringDelay
                ? DOTween.Sequence()
                    .SetTarget(target)
                    .SetEase(Ease.Linear)
                    .AppendInterval(_delay)
                    .AppendCallback(() => target.localScale = startScale)
                    .Append(scaleTween)
                : scaleTween.SetDelay(_delay);

            return playback
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
