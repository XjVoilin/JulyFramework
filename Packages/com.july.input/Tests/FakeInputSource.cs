using System;
using System.Collections.Generic;
using July.Arch;
using UnityEngine;

namespace July.Input.Tests
{
    internal sealed class FakeInputSource : IInputSource
    {
        private Touch[] touches = Array.Empty<Touch>();
        private readonly HashSet<KeyCode> heldKeys = new();
        private readonly HashSet<KeyCode> downKeys = new();

        public int FrameCount { get; private set; }
        public float UnscaledTime { get; private set; }
        public float ShortSide { get; set; } = 1080f;
        public bool IsFocused { get; private set; } = true;
        public int TouchCount => touches.Length;
        public Vector2 MousePosition { get; set; }
        public bool MouseDown { get; set; }
        public bool MouseHeld { get; set; }
        public bool MouseUp { get; set; }
        public bool UIUsesKeyboard { get; set; }
        public Func<Vector2, bool> UIHitTest { get; set; } = _ => false;
        public int TouchReads { get; private set; }

        public event Action<bool> FocusChanged;

        public Touch GetTouch(int index)
        {
            TouchReads++;
            return touches[index];
        }

        public bool GetKey(KeyCode key) => heldKeys.Contains(key);
        public bool GetKeyDown(KeyCode key) => downKeys.Contains(key);
        public bool IsOverUI(Vector2 position) => UIHitTest(position);

        public void NextFrame(params Touch[] frameTouches)
        {
            FrameCount++;
            UnscaledTime += 1f / 60f;
            touches = frameTouches;
            MouseDown = false;
            MouseUp = false;
            downKeys.Clear();
        }

        public void PressMouse(Vector2 position)
        {
            NextFrame();
            MousePosition = position;
            MouseDown = true;
            MouseHeld = true;
        }

        public void MoveMouse(Vector2 position)
        {
            NextFrame();
            MousePosition = position;
            MouseHeld = true;
        }

        public void ReleaseMouse(Vector2 position)
        {
            NextFrame();
            MousePosition = position;
            MouseHeld = false;
            MouseUp = true;
        }

        public void PressKey(KeyCode key)
        {
            heldKeys.Add(key);
            downKeys.Add(key);
        }

        public void ReleaseKey(KeyCode key) => heldKeys.Remove(key);

        public void SetFocus(bool focused)
        {
            IsFocused = focused;
            FocusChanged?.Invoke(focused);
        }

        public static Touch TouchAt(int id, Vector2 position, TouchPhase phase)
            => new Touch { fingerId = id, position = position, phase = phase };
    }

    internal sealed class InputTestContext : IDisposable
    {
        private readonly ArchContext context = new();

        public FakeInputSource Source { get; } = new();
        public UnityInputSystem Input { get; }

        public InputTestContext(InputConfig config = null)
        {
            Input = new UnityInputSystem(config ?? new InputConfig(), Source);
            context.RegisterSystem(Input);
            context.InitializeAsync().GetAwaiter().GetResult();
        }

        public void Dispose() => context.Shutdown();
    }
}
