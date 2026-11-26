using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace July.Animation
{
    /// <summary>启用时从透明淡入到 Graphic 的初始透明度，停止时恢复初始透明度。</summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class UIFadeIn : SimpleAnimationBase
    {
        [Min(0.001f)]
        [SerializeField] private float _duration = 0.25f;
        [SerializeField] private Ease _ease = Ease.OutSine;

        private Graphic _graphic;
        private float _initialAlpha;

        private void Awake()
        {
            _graphic = GetComponent<Graphic>();
            _initialAlpha = _graphic.color.a;
        }

        protected override Tween OnCreateTween()
        {
            SetAlpha(0f);
            return _graphic.DOFade(_initialAlpha, _duration).SetEase(_ease).SetRecyclable(false);
        }

        protected override void OnReset() => SetAlpha(_initialAlpha);

        private void SetAlpha(float alpha)
        {
            var color = _graphic.color;
            color.a = alpha;
            _graphic.color = color;
        }
    }
}
