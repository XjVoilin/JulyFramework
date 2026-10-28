using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace July.Input.Tests
{
    public class InputPointerReuseTests
    {
        [Test]
        public void RepeatedBeganWithinOneFrame_PreservesNewPressRecognition()
        {
            using var test = new InputTestContext();
            var clicks = 0;
            test.Input.Clicked += _ => clicks++;
            test.Source.NextFrame(FakeInputSource.TouchAt(0, Vector2.zero, TouchPhase.Began),
                FakeInputSource.TouchAt(0, Vector2.one * 10f, TouchPhase.Began));
            test.Input.OnUpdate(0f);
            test.Source.NextFrame(FakeInputSource.TouchAt(0, Vector2.one * 10f, TouchPhase.Ended));
            test.Input.OnUpdate(0f);
            Assert.That(clicks, Is.EqualTo(1), "Canceling the old press must not discard the new Began later in the same frame.");
        }

        [Test]
        public void RecycledTouchId_CancelsOldPressAndStartsNewPress()
        {
            using var test = new InputTestContext();
            var cancellations = new List<PointerCancellation>();
            var clicks = 0;
            test.Input.PointerCanceled += cancellations.Add;
            test.Input.Clicked += _ => clicks++;
            test.Source.NextFrame(FakeInputSource.TouchAt(0, Vector2.zero, TouchPhase.Began));
            test.Input.OnUpdate(0f);
            var original = test.Input.ReadFrame();

            test.Source.NextFrame(FakeInputSource.TouchAt(0, Vector2.one * 10f, TouchPhase.Began));
            var frame = test.Input.ReadFrame();
            Assert.That(frame.PointerCount, Is.EqualTo(2));
            Assert.That(frame.GetPointer(0).Phase, Is.EqualTo(PointerPhase.Canceled));
            Assert.That(frame.GetPointer(0).Id, Is.Zero);
            Assert.That(frame.GetPointer(1).Phase, Is.EqualTo(PointerPhase.Began));
            Assert.That(frame.GetPointer(1).StartPosition, Is.EqualTo(Vector2.one * 10f));
            Assert.That(frame.GetPointer(1).StartTime, Is.EqualTo(frame.Time));
            Assert.That(original.PointerCount, Is.EqualTo(1));
            Assert.That(original.GetPointer(0).Phase, Is.EqualTo(PointerPhase.Began));
            test.Input.OnUpdate(0f);
            test.Input.ReadFrame();
            test.Input.OnUpdate(0f);
            Assert.That(cancellations.Count, Is.EqualTo(1));
            Assert.That(cancellations[0].Reason, Is.EqualTo(PointerCancellationReason.DeviceLost));
            Assert.That(clicks, Is.Zero);

            test.Source.NextFrame(FakeInputSource.TouchAt(0, Vector2.one * 10f, TouchPhase.Ended));
            test.Input.OnUpdate(0f);
            Assert.That(clicks, Is.EqualTo(1), "Only the new press may complete a click.");
        }

        [Test]
        public void RecycledTouchBeginningOverUI_CancelsOldGameplayPress()
        {
            using var test = new InputTestContext();
            var cancellations = 0;
            var clicks = 0;
            test.Input.PointerCanceled += _ => cancellations++;
            test.Input.Clicked += _ => clicks++;
            test.Source.NextFrame(FakeInputSource.TouchAt(0, Vector2.zero, TouchPhase.Began));
            test.Input.OnUpdate(0f);
            test.Source.UIHitTest = position => position.x > 50f;
            test.Source.NextFrame(FakeInputSource.TouchAt(0, Vector2.one * 100f, TouchPhase.Began));
            var frame = test.Input.ReadFrame();
            Assert.That(frame.PointerCount, Is.EqualTo(1));
            Assert.That(frame.GetPointer(0).Phase, Is.EqualTo(PointerPhase.Canceled));
            test.Input.OnUpdate(0f);
            test.Source.NextFrame(FakeInputSource.TouchAt(0, Vector2.zero, TouchPhase.Ended));
            test.Input.OnUpdate(0f);
            Assert.That(cancellations, Is.EqualTo(1));
            Assert.That(clicks, Is.Zero, "A UI press must not finish the stale gameplay press.");
        }

        [Test]
        public void RecycledTouch_DoesNotResetOtherActiveFingers()
        {
            using var test = new InputTestContext();
            test.Source.NextFrame(FakeInputSource.TouchAt(0, Vector2.zero, TouchPhase.Began),
                FakeInputSource.TouchAt(1, Vector2.one, TouchPhase.Began));
            test.Input.OnUpdate(0f);
            test.Source.NextFrame(FakeInputSource.TouchAt(0, Vector2.right, TouchPhase.Began),
                FakeInputSource.TouchAt(1, Vector2.one * 2f, TouchPhase.Moved));
            var frame = test.Input.ReadFrame();
            Assert.That(frame.PointerCount, Is.EqualTo(3));
            Assert.That(frame.GetPointer(2).Id, Is.EqualTo(1));
            Assert.That(frame.GetPointer(2).Phase, Is.EqualTo(PointerPhase.Moved));
            Assert.That(frame.GetPointer(2).StartPosition, Is.EqualTo(Vector2.one));
            Assert.That(frame.GetPointer(2).Delta, Is.EqualTo(Vector2.one));
        }
    }
}
