using UnityEngine;

namespace July.Input
{
    public interface IInputGate
    {
        bool IsBlocked { get; }
        void Block();
        void Unblock();
    }

    public interface IInputSystem : IInputGate
    {
        bool ShouldBlockInput(int fingerId = -1);
        bool GetPointerDown(out Vector2 screenPosition);
        bool GetPointerHeld(out Vector2 screenPosition);
        bool GetPointerUp(out Vector2 screenPosition);
        Vector2 PointerScreenPosition { get; }
        int TouchCount { get; }
        bool TryGetTouch(int index, out Touch touch);
    }
}
