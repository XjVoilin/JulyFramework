using DG.Tweening;
using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// 水平滑动：当前页滑出 + 新页滑入。
    /// </summary>
    public class PageTransitionSlide : PageTransition
    {
        [SerializeField] private float _duration = 0.3f;
        [SerializeField] private Ease _ease = Ease.OutCubic;

        public override Tween Play(PageTransitionContext ctx)
        {
            var sign = (int)ctx.Direction;
            var dist = ctx.SlideDistance;
            var fromPos = ctx.FromOriginalPosition;
            var toPos = ctx.ToOriginalPosition;

            ctx.ToPage.anchoredPosition = new Vector2(toPos.x + sign * dist, toPos.y);

            var seq = DOTween.Sequence();
            seq.Join(ctx.FromPage.DOAnchorPosX(fromPos.x - sign * dist, _duration).SetEase(_ease));
            seq.Join(ctx.ToPage.DOAnchorPosX(toPos.x, _duration).SetEase(_ease));
            return seq;
        }
    }
}
