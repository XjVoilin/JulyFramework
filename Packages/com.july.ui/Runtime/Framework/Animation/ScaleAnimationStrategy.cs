using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace July.UI
{
    internal sealed class ScaleAnimationStrategy : IUIAnimationStrategy
    {
        public static readonly ScaleAnimationStrategy Instance = new();

        public async UniTask PlayAsync(GameObject target, bool isOpen, CancellationToken ct)
        {
            var t = target.transform;
            t.DOKill();

            if (isOpen)
            {
                t.localScale = Vector3.one * UIAnimationConstants.ScaleIn;
                await t.DOScale(Vector3.one, UIAnimationConstants.DefaultDuration)
                    .SetEase(Ease.OutBack, 3f)
                    .SetUpdate(true)
                    .SetAutoKill(true)
                    .ToUniTask(cancellationToken: ct);
            }
            else
            {
                t.localScale = Vector3.one;
                await t.DOScale(Vector3.one * UIAnimationConstants.ScaleOut, UIAnimationConstants.DefaultDuration)
                    .SetEase(Ease.InBack, 0.5f)
                    .SetUpdate(true)
                    .SetAutoKill(true)
                    .ToUniTask(cancellationToken: ct);
            }
        }
    }
}
