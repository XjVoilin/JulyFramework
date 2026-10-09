using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace July.Input.Tests
{
    public class InputGateTests
    {
        [Test]
        public void ScopedBlocks_ReleaseOnlyTheirOwnContribution()
        {
            using var test = new InputTestContext();
            IInputGate gate = test.Input;
            var changes = 0;
            gate.BlockStateChanged += () => changes++;
            var gameplay = gate.Block(InputScope.Gameplay);
            var all = gate.Block(InputScope.All);
            Assert.That(gate.IsBlocked(InputScope.UI), Is.True);
            all.Dispose();
            all.Dispose();
            Assert.That(gate.IsBlocked(InputScope.UI), Is.False);
            Assert.That(gate.IsBlocked(InputScope.Gameplay), Is.True);
            gameplay.Dispose();
            Assert.That(gate.IsBlocked(InputScope.All), Is.False);
            Assert.That(changes, Is.EqualTo(4));
        }

        [Test]
        public void BlockThenImmediateRelease_CancelsCurrentGesture_AndWaitsForPhysicalRelease()
        {
            using var test = new InputTestContext();
            var source = test.Source;
            var input = test.Input;
            var directions = 0;
            var clicks = 0;
            var cancellations = new List<PointerCancellation>();
            input.Direction += _ => directions++;
            input.Clicked += _ => clicks++;
            input.PointerCanceled += cancellations.Add;
            source.PressMouse(Vector2.zero);
            input.OnUpdate(0f);
            input.Block(InputScope.Gameplay).Dispose();
            Assert.That(input.IsBlocked(InputScope.Gameplay), Is.False);
            Assert.That(cancellations.Count, Is.EqualTo(1));
            Assert.That(cancellations[0].Reason, Is.EqualTo(PointerCancellationReason.Blocked));

            source.MoveMouse(Vector2.right * 100f);
            input.ReadFrame();
            input.ReadFrame();
            input.OnUpdate(0f);
            source.ReleaseMouse(Vector2.right * 100f);
            input.OnUpdate(0f);
            Assert.That(directions, Is.Zero);
            Assert.That(clicks, Is.Zero);

            source.NextFrame();
            input.OnUpdate(0f);
            source.PressMouse(Vector2.zero);
            input.OnUpdate(0f);
            source.ReleaseMouse(Vector2.right * 100f);
            input.OnUpdate(0f);
            Assert.That(directions, Is.EqualTo(1));
            Assert.That(clicks, Is.Zero);
        }

        [Test]
        public void UiOnlyBlock_DoesNotCancelOrSuppressGameplayPointers()
        {
            using var test = new InputTestContext();
            var input = test.Input;
            var cancellations = 0;
            input.PointerCanceled += _ => cancellations++;
            test.Source.PressMouse(Vector2.zero);
            var initial = input.ReadFrame();
            using (input.Block(InputScope.UI))
            {
                var frame = input.ReadFrame();
                Assert.That(frame.GetPointer(0).IsActive, Is.True);
                Assert.That(frame.ResetVersion, Is.EqualTo(initial.ResetVersion));
                Assert.That(cancellations, Is.Zero);
            }
        }

        [Test]
        public void FocusLoss_CancelsImmediatelyAndDoesNotProduceAClick()
        {
            using var test = new InputTestContext();
            var input = test.Input;
            var clicks = 0;
            var cancellations = new List<PointerCancellation>();
            input.Clicked += _ => clicks++;
            input.PointerCanceled += cancellations.Add;
            test.Source.PressMouse(Vector2.one);
            input.OnUpdate(0f);
            var version = input.ReadFrame().ResetVersion;
            test.Source.SetFocus(false);
            Assert.That(cancellations.Count, Is.EqualTo(1));
            Assert.That(cancellations[0].Reason, Is.EqualTo(PointerCancellationReason.FocusLost));
            Assert.That(input.ReadFrame().ResetVersion, Is.GreaterThan(version));
            test.Source.SetFocus(true);
            test.Source.ReleaseMouse(Vector2.one);
            input.OnUpdate(0f);
            Assert.That(clicks, Is.Zero);
        }

        [Test]
        public void KeyboardCallbackBlockingAndUnblocking_SuppressesThePendingPointerClick()
        {
            using var test = new InputTestContext();
            var input = test.Input;
            var clicks = 0;
            var directions = 0;
            input.Clicked += _ => clicks++;
            input.Direction += _ =>
            {
                directions++;
                input.Block(InputScope.Gameplay).Dispose();
            };
            test.Source.PressMouse(Vector2.zero);
            input.OnUpdate(0f);
            test.Source.ReleaseMouse(Vector2.zero);
            test.Source.PressKey(KeyCode.RightArrow);
            input.ReadFrame();
            input.OnUpdate(0f);
            Assert.That(directions, Is.EqualTo(1));
            Assert.That(input.IsBlocked(InputScope.Gameplay), Is.False);
            Assert.That(clicks, Is.Zero);
        }

        [Test]
        public void UiKeyboardFocus_SuppressesKeyboardDirectionWithoutSuppressingPointers()
        {
            using var test = new InputTestContext();
            var directions = 0;
            test.Input.Direction += _ => directions++;
            test.Source.UIUsesKeyboard = true;
            test.Source.PressMouse(Vector2.one);
            test.Source.PressKey(KeyCode.RightArrow);
            test.Input.OnUpdate(0f);
            Assert.That(directions, Is.Zero);
            Assert.That(test.Input.ReadFrame().GetPointer(0).Phase, Is.EqualTo(PointerPhase.Began));
        }

        [Test]
        public void Swipe_RemainsExclusiveWithClick_AndLeavesTheBaseEndSampleReadable()
        {
            using var test = new InputTestContext();
            test.Source.ShortSide = 2160f;
            var clicks = 0;
            InputDirection? direction = null;
            test.Input.Clicked += _ => clicks++;
            test.Input.Direction += value => direction = value;
            test.Source.NextFrame(
                FakeInputSource.TouchAt(1, Vector2.zero, TouchPhase.Began),
                FakeInputSource.TouchAt(2, Vector2.zero, TouchPhase.Began));
            test.Input.OnUpdate(0f);
            test.Source.NextFrame(
                FakeInputSource.TouchAt(1, Vector2.zero, TouchPhase.Stationary),
                FakeInputSource.TouchAt(2, Vector2.up * 200f, TouchPhase.Ended));
            test.Input.OnUpdate(0f);
            Assert.That(direction, Is.Null);
            test.Source.NextFrame(FakeInputSource.TouchAt(1, Vector2.right * 200f, TouchPhase.Ended));
            test.Input.OnUpdate(0f);
            Assert.That(direction, Is.EqualTo(InputDirection.Right));
            Assert.That(clicks, Is.Zero);
            Assert.That(test.Input.ReadFrame().GetPointer(0).Phase, Is.EqualTo(PointerPhase.Ended));
        }

        [Test]
        public void MovingOutAndBack_DoesNotTurnIntoClick()
        {
            using var test = new InputTestContext();
            var clicks = 0;
            test.Input.Clicked += _ => clicks++;
            test.Source.PressMouse(Vector2.zero);
            test.Input.OnUpdate(0f);
            test.Source.MoveMouse(Vector2.right * 100f);
            test.Input.OnUpdate(0f);
            test.Source.ReleaseMouse(Vector2.zero);
            test.Input.OnUpdate(0f);
            Assert.That(clicks, Is.Zero);
            test.Source.PressMouse(Vector2.zero);
            test.Input.OnUpdate(0f);
            test.Source.ReleaseMouse(Vector2.one);
            test.Input.OnUpdate(0f);
            Assert.That(clicks, Is.EqualTo(1));
        }

        [Test]
        public void DeviceCancellationCallbackCanBlockGameplay_AndCancelTheOtherFingerOnce()
        {
            using var test = new InputTestContext();
            var source = test.Source;
            var input = test.Input;
            var notices = new List<PointerCancellation>();
            var observedFrames = new List<InputFrame>();
            var clicks = 0;
            input.Clicked += _ => clicks++;
            input.PointerCanceled += cancellation =>
            {
                notices.Add(cancellation);
                if (cancellation.Id == 4) input.Block(InputScope.Gameplay).Dispose();
                // Even nested callbacks must see committed cancellation state.
                observedFrames.Add(input.ReadFrame());
            };
            source.NextFrame(
                FakeInputSource.TouchAt(4, Vector2.zero, TouchPhase.Began),
                FakeInputSource.TouchAt(9, Vector2.one, TouchPhase.Began));
            input.OnUpdate(0f);
            var version = input.ReadFrame().ResetVersion;
            source.NextFrame(
                FakeInputSource.TouchAt(4, Vector2.zero, TouchPhase.Canceled),
                FakeInputSource.TouchAt(9, Vector2.one, TouchPhase.Stationary));
            input.OnUpdate(0f);

            Assert.That(notices.FindAll(item => item.Id == 4).Count, Is.EqualTo(1));
            Assert.That(notices.FindAll(item => item.Id == 9).Count, Is.EqualTo(1));
            Assert.That(notices.Find(item => item.Id == 4).Reason,
                Is.EqualTo(PointerCancellationReason.DeviceCanceled));
            Assert.That(notices.Find(item => item.Id == 9).Reason,
                Is.EqualTo(PointerCancellationReason.Blocked));
            Assert.That(input.IsBlocked(InputScope.Gameplay), Is.False);
            Assert.That(observedFrames.Count, Is.EqualTo(2));
            foreach (var frame in observedFrames)
            {
                Assert.That(frame.ResetVersion, Is.GreaterThan(version));
                AssertNoActivePointers(frame);
            }

            source.NextFrame(
                FakeInputSource.TouchAt(4, Vector2.zero, TouchPhase.Ended),
                FakeInputSource.TouchAt(9, Vector2.one, TouchPhase.Ended));
            input.OnUpdate(0f);
            Assert.That(notices.Count, Is.EqualTo(2));
            Assert.That(clicks, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GlobalCancellationCallbackCanReenter_WithoutDuplicateNotifications(bool cancelAgain)
        {
            using var test = new InputTestContext();
            var input = test.Input;
            var notices = new List<PointerCancellation>();
            var observedFrames = new List<InputFrame>();
            var reentered = false;
            var clicks = 0;
            input.Clicked += _ => clicks++;
            input.PointerCanceled += cancellation =>
            {
                notices.Add(cancellation);
                observedFrames.Add(input.ReadFrame());
                if (reentered) return;
                reentered = true;
                if (cancelAgain) input.CancelPointer();
                else input.OnUpdate(0f);
            };
            test.Source.NextFrame(
                FakeInputSource.TouchAt(4, Vector2.zero, TouchPhase.Began),
                FakeInputSource.TouchAt(9, Vector2.one, TouchPhase.Began));
            input.ReadFrame();
            input.CancelPointer();

            Assert.That(notices.FindAll(item => item.Id == 4).Count, Is.EqualTo(1));
            Assert.That(notices.FindAll(item => item.Id == 9).Count, Is.EqualTo(1));
            Assert.That(observedFrames.Count, Is.EqualTo(2));
            foreach (var frame in observedFrames) AssertNoActivePointers(frame);
            input.OnUpdate(0f);
            input.CancelPointer();
            Assert.That(notices.Count, Is.EqualTo(2));
            Assert.That(clicks, Is.Zero);
        }

        private static void AssertNoActivePointers(InputFrame frame)
        {
            for (var i = 0; i < frame.PointerCount; i++)
                Assert.That(frame.GetPointer(i).IsActive, Is.False);
        }
    }
}
