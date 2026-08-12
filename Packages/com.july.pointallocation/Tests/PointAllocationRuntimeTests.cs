using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace July.PointAllocation.Tests
{
    public sealed class PointAllocationRuntimeTests
    {
        private PointAllocationSystem _system;
        private PointAllocationGraphDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            Assert.That(PointAllocationGraphDefinition.TryCreate(
                10,
                new[]
                {
                    new PointAllocationNodeDefinition(1, 2, new[] { 1, 2 }),
                    new PointAllocationNodeDefinition(2, 1, new[] { 1 })
                },
                new[] { new PointAllocationConnectionDefinition(1, 2, 2) },
                out _definition,
                out var errors), Is.True, string.Join("\n", errors));

            _system = new PointAllocationSystem();
            Assert.That(_system.RegisterDefinition(_definition).Success, Is.True);
        }

        [Test]
        public void CreateRuntime_SameDefinition_ProducesIndependentProgress()
        {
            Assert.That(_system.CreateRuntime(
                10,
                PointAllocationSnapshot.Empty(5),
                out var first).Success, Is.True);
            Assert.That(_system.CreateRuntime(
                10,
                PointAllocationSnapshot.Empty(5),
                out var second).Success, Is.True);

            Assert.That(first.AddRank(1).Success, Is.True);

            Assert.That(first.AvailablePoints, Is.EqualTo(4));
            Assert.That(second.AvailablePoints, Is.EqualTo(5));
            Assert.That(second.TryGetNodeState(1, out var state), Is.True);
            Assert.That(state.CurrentRank, Is.Zero);
        }

        [Test]
        public void AddRank_RequiresEveryIncomingConnectionAndEnoughPoints()
        {
            var runtime = CreateRuntime(4);

            var blocked = runtime.AddRank(2);
            Assert.That(blocked.Success, Is.False);
            Assert.That(blocked.FailureReason, Is.EqualTo(PointAllocationFailureReason.PrerequisiteNotMet));
            Assert.That(blocked.RelatedNodeId, Is.EqualTo(1));

            Assert.That(runtime.AddRank(1).Success, Is.True);
            Assert.That(runtime.AddRank(1).Success, Is.True);
            Assert.That(runtime.AddRank(2).Success, Is.True);
            Assert.That(runtime.AvailablePoints, Is.Zero);
        }

        [Test]
        public void RefundRank_BlocksWhenInvestedDependentWouldBecomeInvalid()
        {
            var runtime = CreateRuntime(4);
            runtime.AddRank(1);
            runtime.AddRank(1);
            runtime.AddRank(2);

            var blocked = runtime.RefundRank(1);
            Assert.That(blocked.Success, Is.False);
            Assert.That(blocked.FailureReason, Is.EqualTo(PointAllocationFailureReason.DependentNodeInvested));
            Assert.That(blocked.RelatedNodeId, Is.EqualTo(2));

            Assert.That(runtime.RefundRank(2).Success, Is.True);
            Assert.That(runtime.RefundRank(1).Success, Is.True);
            Assert.That(runtime.AvailablePoints, Is.EqualTo(3));
        }

        [Test]
        public void Reset_RefundsCurrentDefinitionCostsInOneEvent()
        {
            var runtime = CreateRuntime(6);
            runtime.AddRank(1);
            runtime.AddRank(1);
            runtime.AddRank(2);

            var events = new List<PointAllocationChangedEvent>();
            runtime.ProgressChanged += events.Add;

            Assert.That(runtime.Reset().Success, Is.True);

            Assert.That(runtime.AvailablePoints, Is.EqualTo(6));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].Reason, Is.EqualTo(PointAllocationChangeReason.Reset));
            Assert.That(events[0].NodeRankChanges, Has.Count.EqualTo(2));
            Assert.That(runtime.GetSnapshot().NodeRanks, Is.Empty);
        }

        [Test]
        public void GrantPoints_RejectsInvalidAmountAndPublishesAtomicChange()
        {
            var runtime = CreateRuntime(1);
            PointAllocationChangedEvent received = default;
            var count = 0;
            runtime.ProgressChanged += eventData =>
            {
                count++;
                received = eventData;
            };

            Assert.That(runtime.GrantPoints(0).FailureReason, Is.EqualTo(PointAllocationFailureReason.InvalidAmount));
            Assert.That(runtime.GrantPoints(2).Success, Is.True);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(received.PreviousAvailablePoints, Is.EqualTo(1));
            Assert.That(received.CurrentAvailablePoints, Is.EqualTo(3));
            Assert.That(received.NodeRankChanges, Is.Empty);
        }

        [Test]
        public void ReplaceProgress_InvalidDependency_IsAtomic()
        {
            var runtime = CreateRuntime(5);
            var before = runtime.GetSnapshot();
            var replacedCount = 0;
            runtime.ProgressReplaced += _ => replacedCount++;

            var result = runtime.ReplaceProgress(new PointAllocationSnapshot(
                1,
                new[] { new PointAllocationNodeRank(2, 1) }));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(PointAllocationFailureReason.InvalidProgress));
            Assert.That(runtime.AvailablePoints, Is.EqualTo(before.AvailablePoints));
            Assert.That(runtime.GetSnapshot().NodeRanks, Is.Empty);
            Assert.That(replacedCount, Is.Zero);
        }

        [Test]
        public void ReplaceProgress_Valid_PublishesOnlyReplacementMarker()
        {
            var runtime = CreateRuntime(5);
            var changedCount = 0;
            var replacedCount = 0;
            runtime.ProgressChanged += _ => changedCount++;
            runtime.ProgressReplaced += _ => replacedCount++;

            var result = runtime.ReplaceProgress(new PointAllocationSnapshot(
                1,
                new[]
                {
                    new PointAllocationNodeRank(2, 1),
                    new PointAllocationNodeRank(1, 2)
                }));

            Assert.That(result.Success, Is.True);
            Assert.That(changedCount, Is.Zero);
            Assert.That(replacedCount, Is.EqualTo(1));
            Assert.That(runtime.GetSnapshot().NodeRanks[0].NodeId, Is.EqualTo(1));
        }

        [Test]
        public void RemoveDefinition_DoesNotInvalidateExistingRuntime()
        {
            var runtime = CreateRuntime(2);
            Assert.That(_system.RemoveDefinition(10), Is.True);

            Assert.That(runtime.AddRank(1).Success, Is.True);
            Assert.That(runtime.Definition, Is.SameAs(_definition));
            Assert.That(_system.CreateRuntime(
                10,
                PointAllocationSnapshot.Empty(),
                out _).FailureReason, Is.EqualTo(PointAllocationFailureReason.DefinitionNotFound));
        }

        private PointAllocationRuntime CreateRuntime(int points)
        {
            var result = _system.CreateRuntime(
                10,
                PointAllocationSnapshot.Empty(points),
                out var runtime);
            Assert.That(result.Success, Is.True);
            return runtime;
        }
    }
}

