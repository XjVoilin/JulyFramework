using NUnit.Framework;

namespace July.Input.Tests
{
    public class InputGateTests
    {
        [Test]
        public void NestedBlocks_RequireMatchingUnblocks()
        {
            IInputGate gate = new UnityInputSystem();

            gate.Block();
            gate.Block();
            gate.Unblock();
            Assert.That(gate.IsBlocked, Is.True);

            gate.Unblock();
            Assert.That(gate.IsBlocked, Is.False);
        }

        [Test]
        public void ExtraUnblock_DoesNotUnderflow()
        {
            IInputGate gate = new UnityInputSystem();
            gate.Unblock();
            Assert.That(gate.IsBlocked, Is.False);
        }
    }
}
