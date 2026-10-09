using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace July.Input.Tests
{
    public class InputFrameTests
    {
        [Test]
        public void DisabledRecognizers_StillExposeTheCompleteMouseLifecycle()
        {
            using var test = new InputTestContext(new InputConfig
            {
                EnableClick = false,
                EnableSwipe = false,
                EnableKeyboard = false
            });
            var source = test.Source;
            var input = test.Input;
            var origin = new Vector2(10f, 20f);

            source.PressMouse(origin);
            var began = input.ReadFrame();
            var first = began.GetPointer(0);
            Assert.That(began.FrameCount, Is.EqualTo(source.FrameCount));
            Assert.That(began.Time, Is.EqualTo(source.UnscaledTime));
            Assert.That(began.PointerCount, Is.EqualTo(1));
            Assert.That(first.Phase, Is.EqualTo(PointerPhase.Began));
            Assert.That(first.IsActive, Is.True);
            Assert.That(first.Position, Is.EqualTo(origin));
            Assert.That(first.StartPosition, Is.EqualTo(origin));
            Assert.That(first.StartTime, Is.EqualTo(source.UnscaledTime));

            source.MoveMouse(origin + Vector2.right * 5f);
            var moved = input.ReadFrame().GetPointer(0);
            Assert.That(moved.Id, Is.EqualTo(first.Id));
            Assert.That(moved.Phase, Is.EqualTo(PointerPhase.Moved));
            Assert.That(moved.Delta, Is.EqualTo(Vector2.right * 5f));
            Assert.That(moved.StartPosition, Is.EqualTo(first.StartPosition));
            Assert.That(moved.StartTime, Is.EqualTo(first.StartTime));

            source.NextFrame();
            Assert.That(input.ReadFrame().GetPointer(0).Phase, Is.EqualTo(PointerPhase.Stationary));

            source.ReleaseMouse(origin + Vector2.right * 5f);
            var ended = input.ReadFrame().GetPointer(0);
            Assert.That(ended.Phase, Is.EqualTo(PointerPhase.Ended));
            Assert.That(ended.IsActive, Is.False);

            source.NextFrame();
            Assert.That(input.ReadFrame().PointerCount, Is.Zero);
            Assert.That(began.GetPointer(0).Phase, Is.EqualTo(PointerPhase.Began),
                "Previously returned frames must remain immutable.");
        }

        [Test]
        public void RepeatedReads_DoNotResampleOrDispatchGestureCallbacks()
        {
            using var test = new InputTestContext();
            var source = test.Source;
            var input = test.Input;
            var clicks = 0;
            input.Clicked += _ => clicks++;
            source.NextFrame(FakeInputSource.TouchAt(7, Vector2.zero, TouchPhase.Began));
            input.ReadFrame();
            var reads = source.TouchReads;
            input.ReadFrame();
            Assert.That(source.TouchReads, Is.EqualTo(reads));
            input.OnUpdate(0f);

            source.NextFrame(FakeInputSource.TouchAt(7, Vector2.one, TouchPhase.Ended));
            var ended = input.ReadFrame();
            input.ReadFrame();
            Assert.That(clicks, Is.Zero);
            input.OnUpdate(0f);
            input.OnUpdate(0f);
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(input.ReadFrame().GetPointer(0).Phase, Is.EqualTo(PointerPhase.Ended));
            Assert.That(ended.GetPointer(0).Phase, Is.EqualTo(PointerPhase.Ended));
        }

        [Test]
        public void TouchOrderChanges_PreserveEachPointersIdentityAndOrigin()
        {
            using var test = new InputTestContext();
            var source = test.Source;
            var input = test.Input;
            source.NextFrame(
                FakeInputSource.TouchAt(4, Vector2.zero, TouchPhase.Began),
                FakeInputSource.TouchAt(9, Vector2.one * 100f, TouchPhase.Began));
            var began = input.ReadFrame();
            Assert.That(began.PointerCount, Is.EqualTo(2));

            source.NextFrame(
                FakeInputSource.TouchAt(9, Vector2.one * 110f, TouchPhase.Moved),
                FakeInputSource.TouchAt(4, Vector2.right * 20f, TouchPhase.Moved));
            var moved = input.ReadFrame();
            Assert.That(Find(moved, 4).StartPosition, Is.EqualTo(Vector2.zero));
            Assert.That(Find(moved, 4).Delta, Is.EqualTo(Vector2.right * 20f));
            Assert.That(Find(moved, 9).StartPosition, Is.EqualTo(Vector2.one * 100f));
            Assert.That(Find(moved, 9).Delta, Is.EqualTo(Vector2.one * 10f));
            Assert.That(Find(began, 4).Position, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void CancelledTouch_DoesNotCancelAnotherFinger_OrCallBackInsideReadFrame()
        {
            using var test = new InputTestContext();
            var source = test.Source;
            var input = test.Input;
            var cancellations = new List<PointerCancellation>();
            input.PointerCanceled += cancellations.Add;
            source.NextFrame(
                FakeInputSource.TouchAt(4, Vector2.zero, TouchPhase.Began),
                FakeInputSource.TouchAt(9, Vector2.one * 100f, TouchPhase.Began));
            input.OnUpdate(0f);

            source.NextFrame(
                FakeInputSource.TouchAt(4, Vector2.right, TouchPhase.Canceled),
                FakeInputSource.TouchAt(9, Vector2.one * 100f, TouchPhase.Stationary));
            var frame = input.ReadFrame();
            Assert.That(Find(frame, 4).Phase, Is.EqualTo(PointerPhase.Canceled));
            Assert.That(Find(frame, 9).IsActive, Is.True);
            Assert.That(cancellations, Is.Empty);
            input.OnUpdate(0f);
            input.OnUpdate(0f);
            Assert.That(cancellations.Count, Is.EqualTo(1));
            Assert.That(cancellations[0].Id, Is.EqualTo(4));
            Assert.That(cancellations[0].Reason, Is.EqualTo(PointerCancellationReason.DeviceCanceled));

            source.NextFrame(FakeInputSource.TouchAt(9, Vector2.one * 110f, TouchPhase.Moved));
            var next = input.ReadFrame();
            Assert.That(next.PointerCount, Is.EqualTo(1));
            Assert.That(next.GetPointer(0).Id, Is.EqualTo(9));
            Assert.That(next.GetPointer(0).IsActive, Is.True);
        }

        [Test]
        public void MissingTouch_IsCancelledInsteadOfTransferredToAnExistingFinger()
        {
            using var test = new InputTestContext();
            var source = test.Source;
            var input = test.Input;
            var cancellations = new List<PointerCancellation>();
            input.PointerCanceled += cancellations.Add;
            source.NextFrame(
                FakeInputSource.TouchAt(4, Vector2.zero, TouchPhase.Began),
                FakeInputSource.TouchAt(9, Vector2.one * 100f, TouchPhase.Began));
            input.OnUpdate(0f);

            source.NextFrame(FakeInputSource.TouchAt(9, Vector2.one * 100f, TouchPhase.Stationary));
            var frame = input.ReadFrame();
            Assert.That(Find(frame, 4).Phase, Is.EqualTo(PointerPhase.Canceled));
            Assert.That(Find(frame, 9).Phase, Is.EqualTo(PointerPhase.Stationary));
            input.OnUpdate(0f);
            Assert.That(cancellations.Count, Is.EqualTo(1));
            Assert.That(cancellations[0].Reason, Is.EqualTo(PointerCancellationReason.DeviceLost));
        }

        [Test]
        public void TouchInput_DoesNotAlsoExposeTheSimulatedMouse()
        {
            using var test = new InputTestContext();
            test.Source.NextFrame(FakeInputSource.TouchAt(6, Vector2.one, TouchPhase.Began));
            test.Source.MouseDown = true;
            test.Source.MouseHeld = true;
            test.Source.MousePosition = Vector2.one;
            var frame = test.Input.ReadFrame();
            Assert.That(frame.PointerCount, Is.EqualTo(1));
            Assert.That(frame.GetPointer(0).Id, Is.EqualTo(6));
        }

        [Test]
        public void UiPress_IsExcluded_WithoutExcludingAnotherFinger()
        {
            using var test = new InputTestContext();
            test.Source.UIHitTest = point => point.x < 0f;
            test.Source.NextFrame(
                FakeInputSource.TouchAt(4, Vector2.left, TouchPhase.Began),
                FakeInputSource.TouchAt(9, Vector2.right, TouchPhase.Began));
            var frame = test.Input.ReadFrame();
            Assert.That(frame.PointerCount, Is.EqualTo(1));
            Assert.That(frame.GetPointer(0).Id, Is.EqualTo(9));

            test.Source.NextFrame(
                FakeInputSource.TouchAt(4, Vector2.right * 2f, TouchPhase.Moved),
                FakeInputSource.TouchAt(9, Vector2.right, TouchPhase.Stationary));
            Assert.That(test.Input.ReadFrame().PointerCount, Is.EqualTo(1));
        }

        [Test]
        public void GameplayPointer_CanCrossUi_AndCallerCanCheckUiAtRelease()
        {
            using var test = new InputTestContext();
            test.Source.UIHitTest = point => point.x >= 50f;
            test.Source.PressMouse(Vector2.zero);
            test.Input.ReadFrame();
            test.Source.MoveMouse(Vector2.right * 100f);
            var moved = test.Input.ReadFrame().GetPointer(0);
            Assert.That(moved.IsActive, Is.True);
            Assert.That(test.Input.IsOverUI(moved.Position), Is.True);
            test.Source.ReleaseMouse(moved.Position);
            var ended = test.Input.ReadFrame().GetPointer(0);
            Assert.That(ended.Phase, Is.EqualTo(PointerPhase.Ended));
            Assert.That(test.Input.IsOverUI(ended.Position), Is.True);
        }

        [Test]
        public void ExplicitCancellation_ReplacesCurrentFrameWithoutMutatingPreviousSnapshot()
        {
            using var test = new InputTestContext();
            test.Source.PressMouse(Vector2.one);
            var before = test.Input.ReadFrame();
            var cancellations = new List<PointerCancellation>();
            test.Input.PointerCanceled += cancellations.Add;
            test.Input.CancelPointer();
            var after = test.Input.ReadFrame();
            Assert.That(after.FrameCount, Is.EqualTo(before.FrameCount));
            Assert.That(after.ResetVersion, Is.GreaterThan(before.ResetVersion));
            Assert.That(after.GetPointer(0).Phase, Is.EqualTo(PointerPhase.Canceled));
            Assert.That(before.GetPointer(0).Phase, Is.EqualTo(PointerPhase.Began));
            Assert.That(cancellations.Count, Is.EqualTo(1));
            Assert.That(cancellations[0].Reason, Is.EqualTo(PointerCancellationReason.Explicit));
            test.Input.CancelPointer();
            Assert.That(cancellations.Count, Is.EqualTo(1));
        }

        [Test]
        public void ResetVersion_ChangesEvenWhenThereAreNoActivePointers()
        {
            using var test = new InputTestContext();
            var before = test.Input.ReadFrame();
            test.Input.CancelPointer();
            var cancelled = test.Input.ReadFrame();
            Assert.That(cancelled.PointerCount, Is.Zero);
            Assert.That(cancelled.ResetVersion, Is.GreaterThan(before.ResetVersion));
            using (test.Input.Block(InputScope.Gameplay))
            {
                Assert.That(test.Input.ReadFrame().ResetVersion, Is.GreaterThan(cancelled.ResetVersion));
            }
        }

        [Test]
        public void DisabledGestureThresholds_DoNotPreventBasicPointerSampling()
        {
            using var test = new InputTestContext(new InputConfig
            {
                EnableClick = false,
                EnableSwipe = false,
                EnableKeyboard = false,
                ReferenceShortSide = float.NaN,
                ClickTolerance = float.NegativeInfinity,
                SwipeDistance = float.NaN
            });
            test.Source.PressMouse(Vector2.one);
            var frame = test.Input.ReadFrame();
            Assert.That(frame.PointerCount, Is.EqualTo(1));
            Assert.That(frame.GetPointer(0).Phase, Is.EqualTo(PointerPhase.Began));
        }

        [Test]
        public void MouseDownAndUpInOneFrame_PreserveBothSamplesAndProduceOneClick()
        {
            using var test = new InputTestContext();
            var clicks = 0;
            test.Input.Clicked += _ => clicks++;
            test.Source.PressMouse(Vector2.one);
            test.Source.MouseHeld = false;
            test.Source.MouseUp = true;
            var frame = test.Input.ReadFrame();
            Assert.That(frame.PointerCount, Is.EqualTo(2));
            Assert.That(frame.GetPointer(0).Phase, Is.EqualTo(PointerPhase.Began));
            Assert.That(frame.GetPointer(1).Phase, Is.EqualTo(PointerPhase.Ended));
            Assert.That(frame.GetPointer(0).Id, Is.EqualTo(frame.GetPointer(1).Id));
            Assert.That(clicks, Is.Zero);
            test.Input.OnUpdate(0f);
            test.Input.OnUpdate(0f);
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(test.Input.ReadFrame().PointerCount, Is.EqualTo(2));
            test.Source.NextFrame();
            Assert.That(test.Input.ReadFrame().PointerCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResetAfterCompletedPress_RemainsObservableAcrossEmptyFrames(bool loseFocus)
        {
            using var test = new InputTestContext();
            test.Source.PressMouse(Vector2.one);
            test.Input.OnUpdate(0f);
            test.Source.ReleaseMouse(Vector2.one);
            test.Input.OnUpdate(0f);
            var completed = test.Input.ReadFrame();
            Assert.That(completed.GetPointer(0).Phase, Is.EqualTo(PointerPhase.Ended));

            var cancellations = 0;
            test.Input.PointerCanceled += _ => cancellations++;
            if (loseFocus)
            {
                test.Source.SetFocus(false);
                test.Source.SetFocus(true);
            }
            else test.Input.Block(InputScope.Gameplay).Dispose();
            var reset = test.Input.ReadFrame();
            Assert.That(reset.PointerCount, Is.Zero);
            Assert.That(reset.ResetVersion, Is.GreaterThan(completed.ResetVersion));
            Assert.That(cancellations, Is.Zero, "A completed press must not be canceled again.");

            for (var i = 0; i < 3; i++)
            {
                test.Source.NextFrame();
                var empty = test.Input.ReadFrame();
                Assert.That(empty.PointerCount, Is.Zero);
                Assert.That(empty.ResetVersion, Is.EqualTo(reset.ResetVersion));
            }
        }

        private static PointerSample Find(InputFrame frame, int id)
        {
            for (var i = 0; i < frame.PointerCount; i++)
            {
                var pointer = frame.GetPointer(i);
                if (pointer.Id == id) return pointer;
            }
            Assert.Fail($"Pointer {id} was not present in frame {frame.FrameCount}.");
            return default;
        }
    }
}
