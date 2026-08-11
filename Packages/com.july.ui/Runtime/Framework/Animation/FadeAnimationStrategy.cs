using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace July.UI
{
    internal sealed class FadeAnimationStrategy : IUIAnimationStrategy
    {
        public static readonly FadeAnimationStrategy Instance = new();

        public async UniTask PlayAsync(GameObject target, bool isOpen, CancellationToken ct)
        {
            var canvasGroup = target.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = target.AddComponent<CanvasGroup>();

            canvasGroup.DOKill();

            if (isOpen)
            {
                canvasGroup.alpha = UIAnimationConstants.FadeStartAlpha;
                await canvasGroup.DOFade(UIAnimationConstants.FadeEndAlpha, UIAnimationConstants.DefaultDuration)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
                    .SetAutoKill(true)
                    .ToUniTask(cancellationToken: ct);
            }
            else
            {
                await canvasGroup.DOFade(UIAnimationConstants.FadeStartAlpha, UIAnimationConstants.DefaultDuration)
                    .SetEase(Ease.InQuad)
                    .SetUpdate(true)
                    .SetAutoKill(true)
                    .ToUniTask(cancellationToken: ct);
            }
        }
    }
}
