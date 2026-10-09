using System;
using UnityEngine;

namespace July.Input
{
    // 设备与 UGUI 边界。测试替换采样源，仍通过 IInputSystem 的正常入口验证状态和时序。
    internal interface IInputSource
    {
        int FrameCount { get; }
        float UnscaledTime { get; }
        float ShortSide { get; }
        bool IsFocused { get; }
        int TouchCount { get; }
        Touch GetTouch(int index);
        Vector2 MousePosition { get; }
        bool MouseDown { get; }
        bool MouseHeld { get; }
        bool MouseUp { get; }
        bool GetKey(KeyCode key);
        bool GetKeyDown(KeyCode key);
        bool UIUsesKeyboard { get; }
        bool IsOverUI(Vector2 position);
        event Action<bool> FocusChanged;
    }
}
