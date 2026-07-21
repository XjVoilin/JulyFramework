using DG.Tweening;
using UnityEngine;

namespace July.UI
{
    /// <summary>
    /// 透明度过渡：离场页淡出 + 入场页淡入。
    /// 需要页面上挂有 CanvasGroup。
    /// </summary>
    public class PageTransitionFade : PageTransition
    {
        [SerializeField] private float _duration = 0.3f;
        [SerializeField] private Ease _ease = Ease.OutCubic;

        public override Tween Play(PageTransitionContext ctx)
        {
            var fromCg = GetOrAddCanvasGroup(ctx.FromPage);
            var toCg = GetOrAddCanvasGroup(ctx.ToPage);

            fromCg.alpha = 1f;
            toCg.alpha = 0f;

            var seq = DOTween.Sequence();
            seq.Join(DOTween.To(() => fromCg.alpha, v => fromCg.alpha = v, 0f, _duration).SetEase(_ease));
            seq.Join(DOTween.To(() => toCg.alpha, v => toCg.alpha = v, 1f, _duration).SetEase(_ease));
            return seq;
        }

        private static CanvasGroup GetOrAddCanvasGroup(Component target)
        {
            var cg = target.GetComponent<CanvasGroup>();
            if (!cg) cg = target.gameObject.AddComponent<CanvasGroup>();
            return cg;
        }
    }
}
