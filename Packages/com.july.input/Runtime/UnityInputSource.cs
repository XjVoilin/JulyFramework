using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace July.Input
{
    internal sealed class UnityInputSource : IInputSource
    {
        private readonly List<RaycastResult> hits = new();
        private EventSystem eventSystem;
        private PointerEventData pointerData;

        public int FrameCount => Time.frameCount;
        public float UnscaledTime => Time.unscaledTime;
        public float ShortSide => Mathf.Min(Screen.width, Screen.height);
        public bool IsFocused => Application.isFocused;
        public int TouchCount => UnityEngine.Input.touchCount;
        public Touch GetTouch(int index) => UnityEngine.Input.GetTouch(index);
        public Vector2 MousePosition => UnityEngine.Input.mousePosition;
        public bool MouseDown => UnityEngine.Input.GetMouseButtonDown(0);
        public bool MouseHeld => UnityEngine.Input.GetMouseButton(0);
        public bool MouseUp => UnityEngine.Input.GetMouseButtonUp(0);
        public bool GetKey(KeyCode key) => UnityEngine.Input.GetKey(key);
        public bool GetKeyDown(KeyCode key) => UnityEngine.Input.GetKeyDown(key);
        public event Action<bool> FocusChanged
        {
            add => Application.focusChanged += value;
            remove => Application.focusChanged -= value;
        }

        public bool UIUsesKeyboard
        {
            get
            {
                var current = EventSystem.current;
                if (current == null || current.currentSelectedGameObject == null) return false;
                return current.sendNavigationEvents ||
                       current.currentSelectedGameObject.GetComponent<IUpdateSelectedHandler>() != null;
            }
        }

        public bool IsOverUI(Vector2 position)
        {
            var current = EventSystem.current;
            if (current == null) return false;
            if (eventSystem != current)
            {
                eventSystem = current;
                pointerData = new PointerEventData(current);
            }
            pointerData.Reset();
            pointerData.position = position;
            hits.Clear();
            // 只判断是否命中 UGUI，无需查询其他 Raycaster 或对全部结果排序。
            var raycasters = RaycasterManager.GetRaycasters();
            for (var i = 0; i < raycasters.Count; i++)
            {
                if (raycasters[i] is not GraphicRaycaster graphic || !graphic.IsActive())
                    continue;

                graphic.Raycast(pointerData, hits);
                if (hits.Count > 0) return true;
            }
            return false;
        }
    }
}
