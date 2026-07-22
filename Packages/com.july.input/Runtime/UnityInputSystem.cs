using System;
using July.Arch;
using UnityEngine;
using UnityEngine.EventSystems;

namespace July.Input
{
    /// <summary>Legacy Unity input implementation with nested blocking and UI hit filtering.</summary>
    public sealed class UnityInputSystem : SystemBase, IInputSystem
    {
        private int _blockCount;

        public bool IsBlocked => _blockCount > 0;
        public void Block() => _blockCount++;
        public void Unblock() => _blockCount = Math.Max(0, _blockCount - 1);

        public bool ShouldBlockInput(int fingerId = -1) =>
            IsBlocked || IsPointerOverGameObject(fingerId);

        public bool GetPointerDown(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
            if (IsBlocked || !GetRawPointerDown(out screenPosition)) return false;
            if (!IsPointerOverGameObject(GetCurrentFingerId())) return true;
            screenPosition = Vector2.zero;
            return false;
        }

        public bool GetPointerHeld(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
            return !IsBlocked && GetRawPointerHeld(out screenPosition);
        }

        public bool GetPointerUp(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
            return !IsBlocked && GetRawPointerUp(out screenPosition);
        }

        public Vector2 PointerScreenPosition
        {
            get
            {
#if UNITY_EDITOR || UNITY_STANDALONE
                return UnityEngine.Input.mousePosition;
#else
                return UnityEngine.Input.touchCount > 0
                    ? UnityEngine.Input.GetTouch(0).position : Vector2.zero;
#endif
            }
        }

        public int TouchCount => IsBlocked ? 0 : UnityEngine.Input.touchCount;

        public bool TryGetTouch(int index, out Touch touch)
        {
            if (!IsBlocked && index >= 0 && index < UnityEngine.Input.touchCount)
            {
                touch = UnityEngine.Input.GetTouch(index);
                return true;
            }
            touch = default;
            return false;
        }

        private static bool GetRawPointerDown(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
#if UNITY_EDITOR || UNITY_STANDALONE
            if (!UnityEngine.Input.GetMouseButtonDown(0)) return false;
            screenPosition = UnityEngine.Input.mousePosition;
            return true;
#else
            if (UnityEngine.Input.touchCount <= 0) return false;
            var touch = UnityEngine.Input.GetTouch(0);
            if (touch.phase != TouchPhase.Began) return false;
            screenPosition = touch.position;
            return true;
#endif
        }

        private static bool GetRawPointerHeld(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
#if UNITY_EDITOR || UNITY_STANDALONE
            if (!UnityEngine.Input.GetMouseButton(0)) return false;
            screenPosition = UnityEngine.Input.mousePosition;
            return true;
#else
            if (UnityEngine.Input.touchCount <= 0) return false;
            var touch = UnityEngine.Input.GetTouch(0);
            if (touch.phase != TouchPhase.Moved && touch.phase != TouchPhase.Stationary) return false;
            screenPosition = touch.position;
            return true;
#endif
        }

        private static bool GetRawPointerUp(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
#if UNITY_EDITOR || UNITY_STANDALONE
            if (!UnityEngine.Input.GetMouseButtonUp(0)) return false;
            screenPosition = UnityEngine.Input.mousePosition;
            return true;
#else
            if (UnityEngine.Input.touchCount <= 0) return false;
            var touch = UnityEngine.Input.GetTouch(0);
            if (touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled) return false;
            screenPosition = touch.position;
            return true;
#endif
        }

        private static bool IsPointerOverGameObject(int fingerId = -1)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            return fingerId >= 0
                ? eventSystem.IsPointerOverGameObject(fingerId)
                : eventSystem.IsPointerOverGameObject();
        }

        private static int GetCurrentFingerId() =>
            UnityEngine.Input.touchCount > 0 ? UnityEngine.Input.GetTouch(0).fingerId : -1;
    }
}
