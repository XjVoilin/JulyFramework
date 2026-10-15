using System;
using System.Collections;
using System.Collections.Generic;
using July.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace July.UI.Tests
{
    public sealed class JulyStandaloneInputModuleTests
    {
        private GameObject root;
        private GameObject target;
        private FocusTestEventSystem events;
        private FocusTestInput device;
        private JulyStandaloneInputModule module;
        private FocusTestGate gate;
        private FocusTestPointerProbe probe;
        private int clicks;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            root = new GameObject("July UI focus test");
            events = root.AddComponent<FocusTestEventSystem>();
            events.sendNavigationEvents = false;
            device = root.AddComponent<FocusTestInput>();
            module = root.AddComponent<JulyStandaloneInputModule>();
            module.inputOverride = device;
            gate = new FocusTestGate();
            module.Bind(gate);
            target = new GameObject("Button", typeof(RectTransform), typeof(Button));
            probe = target.AddComponent<FocusTestPointerProbe>();
            clicks = 0;
            target.GetComponent<Button>().onClick.AddListener(() => clicks++);
            root.AddComponent<FocusTestRaycaster>().Target = target;
            events.SetFocus(true);
            module.ActivateModule();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            module.Unbind();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(target);
            yield return null;
        }

        private void Step()
        {
            module.UpdateModule();
            module.Process();
        }

        [UnityTest]
        public IEnumerator FocusRecovery_FirstMouseClickReachesButton()
        {
            events.SetFocus(false);
            yield return null;
            device.Mouse(true, true, false);
            events.SetFocus(true);
            Step();
            Assert.That(probe.Downs, Is.EqualTo(1));
            yield return null;
            device.Mouse(false, false, true);
            Step();
            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FocusRecovery_FirstTouchTapReachesButton()
        {
            events.SetFocus(false);
            yield return null;
            device.Touches(TouchAt(4, TouchPhase.Began));
            events.SetFocus(true);
            Step();
            Assert.That(probe.Downs, Is.EqualTo(1));
            yield return null;
            device.Touches(TouchAt(4, TouchPhase.Ended));
            Step();
            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FocusLoss_CancelsOldMouseWithoutClicking()
        {
            device.Mouse(true, true, false);
            events.SetFocus(true);
            Step();
            Assert.That(probe.Downs, Is.EqualTo(1));
            events.SetFocus(false);
            Assert.That(probe.Ups, Is.EqualTo(1));
            Assert.That(clicks, Is.Zero);
            yield return null;
            device.Mouse(false, true, false);
            events.SetFocus(true);
            Step();
            Assert.That(probe.Downs, Is.EqualTo(1));
            yield return null;
            device.Mouse(false, false, true);
            Step();
            Assert.That(clicks, Is.Zero);
            Assert.That(probe.Ups, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FocusLoss_EndsDragWithoutDropping()
        {
            device.Mouse(true, true, false);
            events.SetFocus(true);
            Step();
            yield return null;
            device.Mouse(false, true, false);
            device.Position += new Vector2(100f, 0f);
            Step();
            Assert.That(probe.DragStarts, Is.EqualTo(1));
            events.SetFocus(false);
            Assert.That(probe.DragEnds, Is.EqualTo(1));
            Assert.That(probe.Drops, Is.Zero);
            Assert.That(clicks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FocusRecovery_DoesNotAdoptOldTouchOrBlockANewFinger()
        {
            device.Touches(TouchAt(4, TouchPhase.Began));
            events.SetFocus(true);
            Step();
            Assert.That(probe.Downs, Is.EqualTo(1));
            events.SetFocus(false);
            Assert.That(probe.Ups, Is.EqualTo(1));
            yield return null;
            device.Touches(TouchAt(4, TouchPhase.Stationary));
            events.SetFocus(true);
            Step();
            Assert.That(probe.Downs, Is.EqualTo(1), "An old touch must not become a new UI press.");
            yield return null;
            device.Touches(TouchAt(4, TouchPhase.Stationary), TouchAt(9, TouchPhase.Began));
            Step();
            Assert.That(probe.Downs, Is.EqualTo(2), "The old finger must not suppress a new finger.");
            yield return null;
            device.Touches(TouchAt(4, TouchPhase.Ended), TouchAt(9, TouchPhase.Ended));
            Step();
            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FocusRecovery_PreservesUIBlockAndItsReleaseWait()
        {
            var block = gate.Block(InputScope.UI);
            events.SetFocus(false);
            yield return null;
            device.Mouse(true, true, false);
            events.SetFocus(true);
            Step();
            Assert.That(gate.IsBlocked(InputScope.UI), Is.True);
            Assert.That(probe.Downs, Is.Zero);
            block.Dispose();
            yield return null;
            device.Mouse(false, true, false);
            Step();
            Assert.That(probe.Downs, Is.Zero);
            yield return null;
            device.Mouse(false, false, true);
            Step();
            Assert.That(clicks, Is.Zero);
            yield return null;
            device.Mouse(true, true, false);
            Step();
            yield return null;
            device.Mouse(false, false, true);
            Step();
            Assert.That(clicks, Is.EqualTo(1));
        }

        private static Touch TouchAt(int id, TouchPhase phase)
            => new() { fingerId = id, phase = phase, position = new Vector2(10f, 10f) };
    }

    internal sealed class FocusTestEventSystem : EventSystem
    {
        protected override void Update() { }
        internal void SetFocus(bool focused) => base.OnApplicationFocus(focused);
    }

    internal sealed class FocusTestInput : BaseInput
    {
        private Touch[] touches = Array.Empty<Touch>();
        private bool down, held, up;
        internal Vector2 Position = new(10f, 10f);
        public override bool mousePresent => true;
        public override Vector2 mousePosition => Position;
        public override Vector2 mouseScrollDelta => Vector2.zero;
        public override bool touchSupported => true;
        public override int touchCount => touches.Length;
        public override Touch GetTouch(int index) => touches[index];
        public override bool GetMouseButtonDown(int button) => button == 0 && down;
        public override bool GetMouseButton(int button) => button == 0 && held;
        public override bool GetMouseButtonUp(int button) => button == 0 && up;
        public override float GetAxisRaw(string axisName) => 0f;
        public override bool GetButtonDown(string buttonName) => false;
        internal void Mouse(bool pressed, bool holding, bool released)
        {
            touches = Array.Empty<Touch>();
            down = pressed; held = holding; up = released;
        }
        internal void Touches(params Touch[] values)
        {
            touches = values;
            down = held = up = false;
        }
    }

    internal sealed class FocusTestRaycaster : BaseRaycaster
    {
        internal GameObject Target;
        public override Camera eventCamera => null;
        public override void Raycast(PointerEventData eventData, List<RaycastResult> results)
            => results.Add(new RaycastResult { gameObject = Target, module = this, screenPosition = eventData.position });
    }

    internal sealed class FocusTestPointerProbe : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        internal int Downs, Ups, DragStarts, DragEnds, Drops;
        public void OnPointerDown(PointerEventData data) => Downs++;
        public void OnPointerUp(PointerEventData data) => Ups++;
        public void OnInitializePotentialDrag(PointerEventData data) => data.useDragThreshold = false;
        public void OnBeginDrag(PointerEventData data) => DragStarts++;
        public void OnDrag(PointerEventData data) { }
        public void OnEndDrag(PointerEventData data) => DragEnds++;
        public void OnDrop(PointerEventData data) => Drops++;
    }

    internal sealed class FocusTestGate : IInputGate
    {
        private int blocks;
        public event Action BlockStateChanged;
        public bool IsBlocked(InputScope scope) => blocks > 0 && (scope & InputScope.UI) != 0;
        public IDisposable Block(InputScope scope)
        {
            blocks++;
            BlockStateChanged?.Invoke();
            return new Lease(this);
        }
        private sealed class Lease : IDisposable
        {
            private readonly FocusTestGate owner;
            internal Lease(FocusTestGate owner) => this.owner = owner;
            public void Dispose()
            {
                owner.blocks--;
                owner.BlockStateChanged?.Invoke();
            }
        }
    }
}
