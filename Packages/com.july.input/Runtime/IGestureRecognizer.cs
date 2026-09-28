using UnityEngine;

namespace July.Input
{
    /// <summary>识别器只接收采集结果，不读取设备、UI 或阻断状态。</summary>
    internal interface IGestureRecognizer
    {
        void Begin(GestureSample sample);
        GestureResult Update(GestureSample sample);
        GestureResult End(GestureSample sample);
        void Cancel();
    }

    internal readonly struct GestureSample
    {
        public readonly Vector2 ScreenPosition;
        // 从按下点开始的位移，单位为配置中的参考像素。
        public readonly Vector2 Displacement;
        public readonly float ElapsedTime;

        public GestureSample(Vector2 screenPosition, Vector2 displacement, float elapsedTime)
        {
            ScreenPosition = screenPosition;
            Displacement = displacement;
            ElapsedTime = elapsedTime;
        }
    }

    internal enum GestureKind { None, Click, Swipe }

    internal readonly struct GestureResult
    {
        public readonly GestureKind Kind;
        public readonly Vector2 ScreenPosition;
        public readonly InputDirection Direction;

        public GestureResult(GestureKind kind, Vector2 screenPosition, InputDirection direction = default)
        {
            Kind = kind;
            ScreenPosition = screenPosition;
            Direction = direction;
        }
    }
}
