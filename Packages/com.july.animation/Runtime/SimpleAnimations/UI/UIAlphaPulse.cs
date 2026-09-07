using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace July.Animation
{
    /// <summary>
    /// UI 透明度呼吸动画：从最低透明度开始，在最低与最高透明度之间往返循环。
    /// 启用时自动播放，停止或禁用时恢复 Awake 时的透明度，保留当前 RGB。
    /// </summary>
    public sealed class UIAlphaPulse : SimpleAnimationBase
    {
        [Tooltip("动画目标；留空时使用当前对象上的 Graphic")]
        [SerializeField] private Graphic _target;

        [Range(0f, 1f)]
        [SerializeField] private float _minAlpha;

        [Range(0f, 1f)]
        [SerializeField] private float _maxAlpha = 1f;

        [Tooltip("单次渐入或渐出的时长(秒)，完整周期为两倍时长")]
        [Min(0.001f)]
        [SerializeField] private float _duration = 0.5f;

        [Tooltip("每次播放时的首次延迟(秒)，循环之间不等待")]
        [Min(0f)]
        [SerializeField] private float _delay = 1f;

        [SerializeField] private Ease _ease = Ease.Linear;

        private float _initialAlpha;

        private void Awake()
        {
            if (_target == null) _target = GetComponent<Graphic>();
            _initialAlpha = _target.color.a;
        }

        protected override Tween OnCreateTween()
        {
            SetAlpha(_minAlpha);
            return _target.DOFade(_maxAlpha, _duration)
                .SetEase(_ease)
                .SetLoops(-1, LoopType.Yoyo)
                .SetDelay(_delay);
        }

        protected override void OnReset()
        {
            SetAlpha(_initialAlpha);
        }

        private void SetAlpha(float alpha)
        {
            var color = _target.color;
            color.a = alpha;
            _target.color = color;
        }
    }
}
