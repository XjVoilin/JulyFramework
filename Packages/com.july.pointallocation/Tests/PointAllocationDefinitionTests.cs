using System;
using NUnit.Framework;

namespace July.PointAllocation.Tests
{
    public sealed class PointAllocationDefinitionTests
    {
        [Test]
        public void TryCreate_ValidDag_CreatesCanonicalDefinition()
        {
            var success = PointAllocationGraphDefinition.TryCreate(
                10,
                new[]
                {
                    new PointAllocationNodeDefinition(2, 1, new[] { 3 }),
                    new PointAllocationNodeDefinition(1, 2, new[] { 1, 2 })
                },
                new[] { new PointAllocationConnectionDefinition(1, 2, 2) },
                out var definition,
                out var errors);

            Assert.That(success, Is.True);
            Assert.That(errors, Is.Empty);
            Assert.That(definition.Id, Is.EqualTo(10));
            Assert.That(definition.Nodes[0].Id, Is.EqualTo(1));
            Assert.That(definition.Connections[0].FromNodeId, Is.EqualTo(1));
        }

        [Test]
        public void Validate_DuplicateNodeAndConnection_ReturnsDetailedErrors()
        {
            var errors = PointAllocationGraphDefinition.Validate(
                1,
                new[]
                {
                    new PointAllocationNodeDefinition(1, 1, new[] { 1 }),
                    new PointAllocationNodeDefinition(1, 1, new[] { 1 }),
                    new PointAllocationNodeDefinition(2, 1, new[] { 1 })
                },
                new[]
                {
                    new PointAllocationConnectionDefinition(1, 2, 1),
                    new PointAllocationConnectionDefinition(1, 2, 1)
                });

            Assert.That(errors, Has.Some.Matches<PointAllocationDefinitionError>(
                error => error.Code == PointAllocationDefinitionErrorCode.DuplicateNodeId));
            Assert.That(errors, Has.Some.Matches<PointAllocationDefinitionError>(
                error => error.Code == PointAllocationDefinitionErrorCode.DuplicateConnection));
        }

        [Test]
        public void Validate_DirectedCycle_IsRejected()
        {
            var errors = PointAllocationGraphDefinition.Validate(
                1,
                new[]
                {
                    new PointAllocationNodeDefinition(1, 1, new[] { 1 }),
                    new PointAllocationNodeDefinition(2, 1, new[] { 1 })
                },
                new[]
                {
                    new PointAllocationConnectionDefinition(1, 2, 1),
                    new PointAllocationConnectionDefinition(2, 1, 1)
                });

            Assert.That(errors, Has.Some.Matches<PointAllocationDefinitionError>(
                error => error.Code == PointAllocationDefinitionErrorCode.DirectedCycle));
        }

        [Test]
        public void Validate_EmptyGraph_IsRejected()
        {
            var errors = PointAllocationGraphDefinition.Validate(
                1,
                Array.Empty<PointAllocationNodeDefinition>(),
                Array.Empty<PointAllocationConnectionDefinition>());

            Assert.That(errors, Has.Some.Matches<PointAllocationDefinitionError>(
                error => error.Code == PointAllocationDefinitionErrorCode.EmptyNodes));
        }

        [Test]
        public void TryCreate_CopiesCallerCollections()
        {
            var costs = new[] { 1, 2 };
            var nodes = new[] { new PointAllocationNodeDefinition(1, 2, costs) };
            Assert.That(PointAllocationGraphDefinition.TryCreate(
                1,
                nodes,
                Array.Empty<PointAllocationConnectionDefinition>(),
                out var definition,
                out _), Is.True);

            costs[0] = 99;
            nodes[0] = new PointAllocationNodeDefinition(5, 1, new[] { 5 });

            Assert.That(definition.Nodes[0].Id, Is.EqualTo(1));
            Assert.That(definition.Nodes[0].RankCosts[0], Is.EqualTo(1));
        }
    }
}
