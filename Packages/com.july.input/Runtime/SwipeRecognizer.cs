using UnityEngine;

namespace July.Input
{
    internal sealed class SwipeRecognizer : IGestureRecognizer
    {
        private readonly float distanceSquared;

        internal SwipeRecognizer(float distance) => distanceSquared = distance * distance;

        public void Begin(GestureSample sample) { }
        public GestureResult Update(GestureSample sample) => default;
        public void Cancel() { } // 滑动只依赖结束样本，无跨样本识别状态。

        public GestureResult End(GestureSample sample)
        {
            var delta = sample.Displacement;
            if (delta.sqrMagnitude < distanceSquared) return default;
            var direction = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                ? delta.x >= 0f ? InputDirection.Right : InputDirection.Left
                : delta.y >= 0f ? InputDirection.Up : InputDirection.Down;
            return new GestureResult(GestureKind.Swipe, sample.ScreenPosition, direction);
        }
    }
}
