using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace July.UI
{
    internal sealed class SlideAnimationStrategy : IUIAnimationStrategy
    {
        private readonly Vector2 _direction;

        public static readonly SlideAnimationStrategy FromTop = new(Vector2.up);
        public static readonly SlideAnimationStrategy FromBottom = new(Vector2.down);
        public static readonly SlideAnimationStrategy FromLeft = new(Vector2.left);
        public static readonly SlideAnimationStrategy FromRight = new(Vector2.right);

        private SlideAnimationStrategy(Vector2 direction)
        {
            _direction = direction;
        }

        public async UniTask PlayAsync(GameObject target, bool isOpen, CancellationToken ct)
        {
            var rect = target.GetComponent<RectTransform>();
            if (rect == null) return;

            rect.DOKill();

            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var canvasRect = canvas.GetComponent<RectTransform>();
            var screenSize = canvasRect != null ? canvasRect.sizeDelta : new Vector2(Screen.width, Screen.height);
            var offset = new Vector2(
                _direction.x * screenSize.x,
                _direction.y * screenSize.y
            );

            if (isOpen)
            {
                var targetPos = rect.anchoredPosition;
                rect.anchoredPosition = offset;
                await rect.DOAnchorPos(targetPos, UIAnimationConstants.DefaultDuration)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true)
                    .SetAutoKill(true)
                    .ToUniTask(cancellationToken: ct);
            }
            else
            {
                await rect.DOAnchorPos(offset, UIAnimationConstants.DefaultDuration)
                    .SetEase(Ease.InCubic)
                    .SetUpdate(true)
                    .SetAutoKill(true)
                    .ToUniTask(cancellationToken: ct);
            }
        }
    }
}
