using DG.Tweening;
using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// 缩放过渡：离场页缩小 + 入场页从小放大。
    /// </summary>
    public class PageTransitionScale : PageTransition
    {
        [SerializeField] private float _duration = 0.3f;
        [SerializeField] private Ease _ease = Ease.OutCubic;
        [SerializeField] private float _scaleFrom = 0.85f;

        public override Tween Play(PageTransitionContext ctx)
        {
            ctx.ToPage.localScale = Vector3.one * _scaleFrom;

            var seq = DOTween.Sequence();
            seq.Join(ctx.FromPage.DOScale(_scaleFrom, _duration).SetEase(_ease));
            seq.Join(ctx.ToPage.DOScale(1f, _duration).SetEase(_ease));
            return seq;
        }
    }
}
