using System;
using System.Collections.Generic;
using July.Arch;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace July.Input
{
    /// <summary>主线程玩法输入：一次认领一个指针，松开时输出点击或四方向滑动。</summary>
    public sealed class UnityInputSystem : SystemBase, IInputSystem, IUpdatableSystem
    {
        private readonly bool enableKeyboard;
        private readonly List<IGestureRecognizer> recognizers = new();
        private readonly float referenceShortSide;
        private readonly KeyCode up, down, left, right;
        private readonly List<RaycastResult> hits = new();
        private EventSystem eventSystem;
        private PointerEventData pointerData;
        private int gameplayBlocks, uiBlocks;
        private bool gameplayNeedsRelease;
        private bool tracking;
        private int pointerId;
        private Vector2 start;
        private float unitsPerPixel;
        private float startedAt;

        public event Action<Vector2> Clicked;
        public event Action<InputDirection> Direction;
        public event Action BlockStateChanged;
        private bool GameplayBlocked => gameplayBlocks > 0;
        private InputScope BlockedScopes => (gameplayBlocks > 0 ? InputScope.Gameplay : 0) |
                                           (uiBlocks > 0 ? InputScope.UI : 0);

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

        public UnityInputSystem() : this(new InputConfig()) { }

        public UnityInputSystem(InputConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate();
            if (config.EnableClick) recognizers.Add(new ClickRecognizer(config.ClickTolerance));
            if (config.EnableSwipe) recognizers.Add(new SwipeRecognizer(config.SwipeDistance));
            enableKeyboard = config.EnableKeyboard;
            referenceShortSide = config.ReferenceShortSide;
            up = config.Up; down = config.Down; left = config.Left; right = config.Right;
        }

        protected override UniTask OnInitializeAsync()
        {
            Application.focusChanged += OnFocusChanged;
            return UniTask.CompletedTask;
        }

        private void OnFocusChanged(bool focused)
        {
            if (!focused)
            {
                CancelPointer();
                gameplayNeedsRelease = true;
            }
        }

        public IDisposable Block(InputScope scope)
        {
            ValidateScope(scope);
            var before = BlockedScopes;
            if ((scope & InputScope.Gameplay) != 0) gameplayBlocks++;
            if ((scope & InputScope.UI) != 0) uiBlocks++;
            if ((before & InputScope.Gameplay) == 0 && GameplayBlocked)
            {
                CancelPointer();
                gameplayNeedsRelease = true;
            }
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

        public void CancelPointer()
        {
            tracking = false;
            foreach (var recognizer in recognizers) recognizer.Cancel();
        }

        public void OnUpdate(float deltaTime)
        {
            if (!Application.isFocused || GameplayBlocked) return;
            // 空闲帧也不分发输入，下一次更新才接收新操作。
            // ArchContext 每帧更新一次，无需再维护单独的恢复帧编号。
            if (gameplayNeedsRelease)
            {
                if (!HasHeldGameplayInput()) gameplayNeedsRelease = false;
                return;
            }
            if (enableKeyboard && !UIUsesKeyboard())
            {
                if (UnityEngine.Input.GetKeyDown(up)) Direction?.Invoke(InputDirection.Up);
                else if (UnityEngine.Input.GetKeyDown(down)) Direction?.Invoke(InputDirection.Down);
                else if (UnityEngine.Input.GetKeyDown(left)) Direction?.Invoke(InputDirection.Left);
                else if (UnityEngine.Input.GetKeyDown(right)) Direction?.Invoke(InputDirection.Right);
            }
            // 键盘回调可能打开阻断玩法输入的窗口。
            if (GameplayBlocked || gameplayNeedsRelease || recognizers.Count == 0) return;

            if (UnityEngine.Input.touchCount > 0 || tracking && pointerId >= 0)
                ReadTouch();
            else
                ReadMouse();
        }

        private bool HasHeldGameplayInput()
            => UnityEngine.Input.touchCount > 0 || UnityEngine.Input.GetMouseButton(0) ||
               enableKeyboard && (UnityEngine.Input.GetKey(up) || UnityEngine.Input.GetKey(down) ||
                                  UnityEngine.Input.GetKey(left) || UnityEngine.Input.GetKey(right));

        private static bool UIUsesKeyboard()
        {
            var current = EventSystem.current;
            if (current == null || current.currentSelectedGameObject == null) return false;
            return current.sendNavigationEvents ||
                   current.currentSelectedGameObject.GetComponent<IUpdateSelectedHandler>() != null;
        }

        private void ReadTouch()
        {
            if (tracking && pointerId < 0) CancelPointer();
            for (var i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                var touch = UnityEngine.Input.GetTouch(i);
                if (tracking)
                {
                    if (touch.fingerId != pointerId) continue;
                    if (touch.phase == TouchPhase.Canceled) CancelPointer();
                    else if (touch.phase == TouchPhase.Ended) EndPointer(touch.fingerId, touch.position);
                    else MovePointer(touch.fingerId, touch.position);
                    return;
                }
                if (touch.phase == TouchPhase.Began)
                {
                    BeginPointer(touch.fingerId, touch.position, ReferenceScale(), IsBlockedByUI(touch.position));
                    return;
                }
            }
            // 失去跟踪的手指不转交给已经按下的其他手指。
            CancelPointer();
        }

        private void ReadMouse()
        {
            Vector2 position = UnityEngine.Input.mousePosition;
            if (UnityEngine.Input.GetMouseButtonDown(0))
                BeginPointer(-1, position, ReferenceScale(), IsBlockedByUI(position));
            if (!tracking) return;
            if (UnityEngine.Input.GetMouseButtonUp(0)) EndPointer(-1, position);
            else if (UnityEngine.Input.GetMouseButton(0)) MovePointer(-1, position);
            else CancelPointer();
        }

        private float ReferenceScale() => referenceShortSide / Mathf.Min(Screen.width, Screen.height);

        private bool IsBlockedByUI(Vector2 position)
        {
            var current = EventSystem.current;
            if (current == null) return false; // 无 UI 的场景仍可接收玩法输入。
            if (eventSystem != current)
            {
                eventSystem = current;
                pointerData = new PointerEventData(current);
            }
            pointerData.Reset();
            pointerData.position = position;
            hits.Clear();
            current.RaycastAll(pointerData, hits);
            // 只检查参与 UGUI 射线的图形；场景 Collider 命中不当作 UI 阻挡。
            foreach (var hit in hits)
                if (hit.module is GraphicRaycaster) return true;
            return false;
        }

        internal void BeginPointer(int id, Vector2 position, float scale, bool blockedByUI)
        {
            if (GameplayBlocked || tracking || blockedByUI || recognizers.Count == 0) return;
            tracking = true;
            pointerId = id;
            start = position;
            unitsPerPixel = scale;
            startedAt = Time.unscaledTime;
            var sample = CreateSample(position);
            foreach (var recognizer in recognizers) recognizer.Begin(sample);
        }

        private GestureSample CreateSample(Vector2 position)
            => new GestureSample(position, (position - start) * unitsPerPixel, Time.unscaledTime - startedAt);

        internal void MovePointer(int id, Vector2 position)
        {
            if (!tracking || id != pointerId) return;
            Recognize(CreateSample(position), false);
        }

        internal void EndPointer(int id, Vector2 position)
        {
            if (!tracking || id != pointerId) return;
            tracking = false;
            Recognize(CreateSample(position), true);
        }

        private void Recognize(GestureSample sample, bool ended)
        {
            var result = default(GestureResult);
            // 当前点击与滑动由配置阈值保证互斥；所有识别器先处理，再统一发布。
            foreach (var recognizer in recognizers)
            {
                var candidate = ended ? recognizer.End(sample) : recognizer.Update(sample);
                if (candidate.Kind != GestureKind.None) result = candidate;
            }
            if (result.Kind == GestureKind.None) return;
            CancelPointer(); // 发布之前结束本次识别，业务回调可以立即修改权限。
            switch (result.Kind)
            {
                case GestureKind.Click:
                    Clicked?.Invoke(result.ScreenPosition);
                    break;
                case GestureKind.Swipe:
                    Direction?.Invoke(result.Direction);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result.Kind));
            }
        }

        protected override void OnShutdown()
        {
            Application.focusChanged -= OnFocusChanged;
            CancelPointer();
            BlockStateChanged = null;
            Clicked = null;
            Direction = null;
            hits.Clear();
            eventSystem = null;
            pointerData = null;
        }
    }
}
