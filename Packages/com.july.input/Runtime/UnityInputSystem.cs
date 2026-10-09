using System;
using System.Collections.Generic;
using July.Arch;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.Input
{
    /// <summary>统一采集玩法指针，并在独立的识别状态上提供可选点击和方向输入。</summary>
    public sealed class UnityInputSystem : SystemBase, IInputSystem, IUpdatableSystem
    {
        private readonly IInputSource source;
        private readonly bool enableKeyboard;
        private readonly List<IGestureRecognizer> recognizers = new();
        private readonly float referenceShortSide;
        private readonly KeyCode up, down, left, right;

        private readonly Dictionary<int, PointerSample> activePointers = new();
        private readonly List<PointerSample> samples = new();
        private readonly HashSet<int> seenPointers = new();
        private readonly List<int> missingPointers = new();
        private readonly Queue<PointerCancellation> cancellations = new();
        private InputFrame currentFrame;
        private int sampledFrame = -1;
        private int processedFrame = -1;
        private uint resetVersion;
        private int gameplayBlocks, uiBlocks;
        private bool gameplayNeedsRelease;
        private bool frameCanRecognize;
        private bool wasFocused;
        private float sampledShortSide;

        // 识别只选择一个指针；它的结束不会修改基础指针集合。
        private bool recognizing;
        private int gesturePointerId;
        private Vector2 gestureStart;
        private float gestureScale;
        private float gestureStartedAt;

        public event Action<PointerCancellation> PointerCanceled;
        public event Action<Vector2> Clicked;
        public event Action<InputDirection> Direction;
        public event Action BlockStateChanged;
        private bool GameplayBlocked => gameplayBlocks > 0;
        private InputScope BlockedScopes => (gameplayBlocks > 0 ? InputScope.Gameplay : 0) |
                                           (uiBlocks > 0 ? InputScope.UI : 0);

        public UnityInputSystem() : this(new InputConfig()) { }
        public UnityInputSystem(InputConfig config) : this(config, new UnityInputSource()) { }

        internal UnityInputSystem(InputConfig config, IInputSource source)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate();
            this.source = source;
            wasFocused = source.IsFocused;
            if (config.EnableClick) recognizers.Add(new ClickRecognizer(config.ClickTolerance));
            if (config.EnableSwipe) recognizers.Add(new SwipeRecognizer(config.SwipeDistance));
            enableKeyboard = config.EnableKeyboard;
            referenceShortSide = config.ReferenceShortSide;
            up = config.Up; down = config.Down; left = config.Left; right = config.Right;
        }

        protected override UniTask OnInitializeAsync()
        {
            source.FocusChanged += OnFocusChanged;
            return UniTask.CompletedTask;
        }

        public InputFrame ReadFrame()
        {
            EnsureFrameSampled();
            return currentFrame;
        }

        public bool IsOverUI(Vector2 screenPosition) => source.IsOverUI(screenPosition);

        public bool IsBlocked(InputScope scope)
        {
            ValidateScope(scope);
            return (BlockedScopes & scope) != 0;
        }

        private static void ValidateScope(InputScope scope)
        {
            if (scope == 0 || (scope & ~InputScope.All) != 0)
                throw new ArgumentOutOfRangeException(nameof(scope));
        }

        public IDisposable Block(InputScope scope)
        {
            ValidateScope(scope);
            var before = BlockedScopes;
            if ((scope & InputScope.Gameplay) != 0) gameplayBlocks++;
            if ((scope & InputScope.UI) != 0) uiBlocks++;
            if ((before & InputScope.Gameplay) == 0 && GameplayBlocked)
                ResetPointers(PointerCancellationReason.Blocked);
            var lease = new InputBlock(this, scope);
            if (before != BlockedScopes) BlockStateChanged?.Invoke();
            return lease;
        }

        private void Release(InputScope scope)
        {
            var before = BlockedScopes;
            if ((scope & InputScope.Gameplay) != 0) gameplayBlocks--;
            if ((scope & InputScope.UI) != 0) uiBlocks--;
            if (before != BlockedScopes) BlockStateChanged?.Invoke();
        }

        private sealed class InputBlock : IDisposable
        {
            private UnityInputSystem owner;
            private readonly InputScope scope;
            internal InputBlock(UnityInputSystem owner, InputScope scope)
            {
                this.owner = owner;
                this.scope = scope;
            }
            public void Dispose()
            {
                var current = owner;
                if (current == null) return;
                owner = null;
                current.Release(scope);
            }
        }

        public void CancelPointer() => ResetPointers(PointerCancellationReason.Explicit);

        private void OnFocusChanged(bool focused)
        {
            wasFocused = focused;
            if (!focused) ResetPointers(PointerCancellationReason.FocusLost);
        }

        private void ResetPointers(PointerCancellationReason reason, bool notify = true)
        {
            resetVersion++;
            gameplayNeedsRelease = true;
            frameCanRecognize = false;
            samples.Clear();
            foreach (var pointer in activePointers.Values)
                RecordCancellation(pointer, reason);
            activePointers.Clear();
            ResetRecognition();
            sampledFrame = source.FrameCount;
            PublishFrame();
            // 状态与快照先提交；事件回调可以重新读取、阻断或取消。
            if (notify) NotifyCancellations();
        }

        private void EnsureFrameSampled()
        {
            if (sampledFrame == source.FrameCount) return;
            sampledFrame = source.FrameCount;
            frameCanRecognize = false;
            samples.Clear();
            if (!source.IsFocused)
            {
                if (wasFocused) ResetPointers(PointerCancellationReason.FocusLost, false);
                wasFocused = false;
                PublishFrame();
                return;
            }
            wasFocused = true;
            if (GameplayBlocked)
            {
                PublishFrame();
                return;
            }
            if (gameplayNeedsRelease)
            {
                if (!HasHeldGameplayInput()) gameplayNeedsRelease = false;
                // 本帧只确认设备归位；下一帧才接受新的按压。
                PublishFrame();
                return;
            }

            frameCanRecognize = true;
            sampledShortSide = source.ShortSide;
            seenPointers.Clear();
            if (source.TouchCount > 0 || HasActiveTouch()) ReadTouches();
            else ReadMouse();
            CancelMissingPointers();
            PublishFrame();
        }

        private void PublishFrame()
        {
            // 值快照不借用可变列表，取消或后续采样不会改变调用方已取得的数据。
            var pointers = samples.Count == 0 ? Array.Empty<PointerSample>() : samples.ToArray();
            currentFrame = new InputFrame(sampledFrame, source.UnscaledTime, resetVersion, pointers);
        }

        private bool HasHeldGameplayInput()
            => source.TouchCount > 0 || source.MouseHeld ||
               enableKeyboard && (source.GetKey(up) || source.GetKey(down) ||
                                  source.GetKey(left) || source.GetKey(right));

        private bool HasActiveTouch()
        {
            foreach (var id in activePointers.Keys)
                if (id >= 0) return true;
            return false;
        }

        private void ReadTouches()
        {
            for (var i = 0; i < source.TouchCount; i++)
            {
                var touch = source.GetTouch(i);
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        BeginPointer(touch.fingerId, touch.position);
                        break;
                    case TouchPhase.Moved:
                        UpdatePointer(touch.fingerId, touch.position, PointerPhase.Moved);
                        break;
                    case TouchPhase.Stationary:
                        UpdatePointer(touch.fingerId, touch.position, PointerPhase.Stationary);
                        break;
                    case TouchPhase.Ended:
                        UpdatePointer(touch.fingerId, touch.position, PointerPhase.Ended);
                        break;
                    case TouchPhase.Canceled:
                        CancelDevicePointer(touch.fingerId, touch.position);
                        break;
                }
            }
        }

        private void ReadMouse()
        {
            var position = source.MousePosition;
            if (source.MouseDown) BeginPointer(-1, position);
            if (source.MouseUp) UpdatePointer(-1, position, PointerPhase.Ended);
            else if (!source.MouseDown && source.MouseHeld)
            {
                if (activePointers.TryGetValue(-1, out var pointer))
                    UpdatePointer(-1, position, position == pointer.Position ? PointerPhase.Stationary : PointerPhase.Moved);
            }
        }

        private void BeginPointer(int id, Vector2 position)
        {
            if (IsOverUI(position)) return;
            var pointer = new PointerSample(id, PointerPhase.Began, position, Vector2.zero, position, source.UnscaledTime);
            activePointers.Add(id, pointer);
            seenPointers.Add(id);
            samples.Add(pointer);
        }

        private void UpdatePointer(int id, Vector2 position, PointerPhase phase)
        {
            // UI 起点、恢复前已按住的指针均没有被接收，不中途接管。
            if (!activePointers.TryGetValue(id, out var previous)) return;
            var pointer = new PointerSample(id, phase, position, position - previous.Position,
                previous.StartPosition, previous.StartTime);
            if (pointer.IsActive) activePointers[id] = pointer;
            else activePointers.Remove(id);
            seenPointers.Add(id);
            samples.Add(pointer);
        }

        private void CancelDevicePointer(int id, Vector2 position)
        {
            if (!activePointers.TryGetValue(id, out var pointer)) return;
            activePointers.Remove(id);
            RecordCancellation(new PointerSample(id, PointerPhase.Canceled, position,
                position - pointer.Position, pointer.StartPosition, pointer.StartTime),
                PointerCancellationReason.DeviceCanceled);
        }

        private void CancelMissingPointers()
        {
            missingPointers.Clear();
            foreach (var id in activePointers.Keys)
                if (!seenPointers.Contains(id)) missingPointers.Add(id);
            foreach (var id in missingPointers)
            {
                var pointer = activePointers[id];
                activePointers.Remove(id);
                RecordCancellation(pointer, PointerCancellationReason.DeviceLost);
            }
        }

        private void RecordCancellation(PointerSample pointer, PointerCancellationReason reason)
        {
            var delta = reason == PointerCancellationReason.DeviceCanceled ? pointer.Delta : Vector2.zero;
            samples.Add(new PointerSample(pointer.Id, PointerPhase.Canceled, pointer.Position,
                delta, pointer.StartPosition, pointer.StartTime));
            cancellations.Enqueue(new PointerCancellation(pointer.Id, pointer.Position, reason));
            if (recognizing && gesturePointerId == pointer.Id) ResetRecognition();
        }

        private void NotifyCancellations()
        {
            while (cancellations.Count > 0)
            {
                var cancellation = cancellations.Dequeue();
                PointerCanceled?.Invoke(cancellation);
            }
        }

        public void OnUpdate(float deltaTime)
        {
            var frame = ReadFrame();
            if (processedFrame == frame.FrameCount) return;
            processedFrame = frame.FrameCount;
            NotifyCancellations();
            if (!CanRecognize(frame)) return;
            PublishKeyboardDirection();
            if (!CanRecognize(frame) || recognizers.Count == 0) return;
            RecognizePointers(frame);
        }

        private bool CanRecognize(InputFrame frame)
            => frameCanRecognize && !GameplayBlocked && source.IsFocused && frame.ResetVersion == resetVersion;

        private void PublishKeyboardDirection()
        {
            if (!enableKeyboard || source.UIUsesKeyboard) return;
            if (source.GetKeyDown(up)) Direction?.Invoke(InputDirection.Up);
            else if (source.GetKeyDown(down)) Direction?.Invoke(InputDirection.Down);
            else if (source.GetKeyDown(left)) Direction?.Invoke(InputDirection.Left);
            else if (source.GetKeyDown(right)) Direction?.Invoke(InputDirection.Right);
        }

        private void RecognizePointers(InputFrame frame)
        {
            for (var i = 0; i < frame.PointerCount; i++)
            {
                var pointer = frame.GetPointer(i);
                if (!recognizing)
                {
                    if (pointer.Phase != PointerPhase.Began) continue;
                    recognizing = true;
                    gesturePointerId = pointer.Id;
                    gestureStart = pointer.Position;
                    gestureStartedAt = pointer.StartTime;
                    gestureScale = referenceShortSide / sampledShortSide;
                    var began = CreateGestureSample(pointer, frame.Time);
                    foreach (var recognizer in recognizers) recognizer.Begin(began);
                    continue;
                }
                if (pointer.Id != gesturePointerId) continue;
                if (pointer.Phase == PointerPhase.Canceled)
                {
                    ResetRecognition();
                    return;
                }
                Recognize(CreateGestureSample(pointer, frame.Time), pointer.Phase == PointerPhase.Ended);
                if (!recognizing || !CanRecognize(frame)) return;
            }
        }

        private GestureSample CreateGestureSample(PointerSample pointer, float time)
            => new GestureSample(pointer.Position, (pointer.Position - gestureStart) * gestureScale,
                time - gestureStartedAt);

        private void Recognize(GestureSample sample, bool ended)
        {
            var result = default(GestureResult);
            // 这里只协调内置点击与滑动；它们由阈值保证互斥。
            foreach (var recognizer in recognizers)
            {
                var candidate = ended ? recognizer.End(sample) : recognizer.Update(sample);
                if (candidate.Kind != GestureKind.None) result = candidate;
            }
            if (ended || result.Kind != GestureKind.None) ResetRecognition();
            switch (result.Kind)
            {
                case GestureKind.None: return;
                case GestureKind.Click: Clicked?.Invoke(result.ScreenPosition); break;
                case GestureKind.Swipe: Direction?.Invoke(result.Direction); break;
                default: throw new ArgumentOutOfRangeException(nameof(result.Kind));
            }
        }

        private void ResetRecognition()
        {
            recognizing = false;
            foreach (var recognizer in recognizers) recognizer.Cancel();
        }

        protected override void OnShutdown()
        {
            source.FocusChanged -= OnFocusChanged;
            ResetPointers(PointerCancellationReason.Shutdown);
            PointerCanceled = null;
            BlockStateChanged = null;
            Clicked = null;
            Direction = null;
        }
    }
}
