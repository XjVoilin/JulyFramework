using System;
using NUnit.Framework;
using UnityEngine;

namespace July.Input.Tests
{
    public class InputGateTests
    {
        [Test]
        public void ScopedBlocks_ReleaseOnlyTheirOwnContribution()
        {
            IInputGate gate = new UnityInputSystem();
            var gameplay = gate.Block(InputScope.Gameplay);
            var all = gate.Block(InputScope.All);
            Assert.That(gate.IsBlocked(InputScope.UI), Is.True);
            all.Dispose();
            all.Dispose();
            Assert.That(gate.IsBlocked(InputScope.UI), Is.False);
            Assert.That(gate.IsBlocked(InputScope.Gameplay), Is.True);
            gameplay.Dispose();
            Assert.That(gate.IsBlocked(InputScope.All), Is.False);
        }

        [Test]
        public void Block_DiscardsCurrentGesture_AndAcceptsOnlyNewPress()
        {
            var input = new UnityInputSystem();
            var count = 0;
            input.Direction += _ => count++;
            input.BeginPointer(1, Vector2.zero, 1f, false);
            input.Block(InputScope.Gameplay).Dispose();
            input.EndPointer(1, Vector2.right * 100);
            Assert.That(count, Is.Zero);
            input.BeginPointer(2, Vector2.zero, 1f, false);
            input.EndPointer(2, Vector2.right * 100);
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void UiPress_AndCancelledPress_DoNotProduceClicks()
        {
            var input = new UnityInputSystem();
            var count = 0;
            input.Clicked += _ => count++;
            input.BeginPointer(1, Vector2.zero, 1f, true);
            input.EndPointer(1, Vector2.zero);
            input.BeginPointer(2, Vector2.zero, 1f, false);
            input.CancelPointer();
            input.EndPointer(2, Vector2.zero);
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void Swipe_IsExclusiveWithClick_AndIgnoresOtherPointer()
        {
            var input = new UnityInputSystem();
            InputDirection? result = null;
            var clicks = 0;
            input.Direction += direction => result = direction;
            input.Clicked += _ => clicks++;
            input.BeginPointer(1, Vector2.zero, 0.5f, false);
            input.BeginPointer(2, Vector2.zero, 0.5f, false);
            input.EndPointer(2, Vector2.up * 200);
            Assert.That(result, Is.Null);
            input.EndPointer(1, Vector2.right * 200);
            Assert.That(result, Is.EqualTo(InputDirection.Right));
            Assert.That(clicks, Is.Zero);
        }

        [Test]
        public void MovingOutAndBack_DoesNotTurnIntoClick()
        {
            var input = new UnityInputSystem();
            var clicks = 0;
            input.Clicked += _ => clicks++;
            input.BeginPointer(1, Vector2.zero, 1f, false);
            input.MovePointer(1, Vector2.right * 100);
            input.EndPointer(1, Vector2.zero);
            Assert.That(clicks, Is.Zero);
            input.BeginPointer(2, Vector2.zero, 1f, false);
            input.EndPointer(2, Vector2.one);
            Assert.That(clicks, Is.EqualTo(1));
        }
    }
}
