using System;
using System.Collections.Generic;
using July.Arch;
using LitJson;
using NUnit.Framework;

namespace July.PointAllocation.Tests
{
    public sealed class PointAllocationSystemTests
    {
        private ArchContext _context;
        private PointAllocationStore _store;
        private PointAllocationSystem _system;

        [SetUp]
        public void SetUp()
        {
            _context = new ArchContext();
            _store = new PointAllocationStore();
            _system = new PointAllocationSystem();
            _context.RegisterStore(_store);
            _context.RegisterSystem(_system);
            _context.InitializeAsync().GetAwaiter().GetResult();
            _system.LoadGraph(CreateGraph10Json());
            _system.LoadGraph(CreateGraph20Json());
        }

        [TearDown]
        public void TearDown() => _context?.Shutdown();

        [Test]
        public void LoadGraph_DuplicateAndMalformedJsonThrow()
        {
            Assert.Throws<InvalidOperationException>(() => _system.LoadGraph(CreateGraph10Json()));

            var context = new ArchContext();
            try
            {
                var system = new PointAllocationSystem();
                context.RegisterStore(new PointAllocationStore());
                context.RegisterSystem(system);
                context.InitializeAsync().GetAwaiter().GetResult();
                system.LoadGraph(CreateGraph10Json());
                Assert.Throws<InvalidOperationException>(() => system.LoadGraph(CreateGraph10Json()));
                var exception = Assert.Throws<ArgumentException>(() => system.LoadGraph("{invalid"));
                Assert.That(exception.Message, Does.Contain("JSON 格式无效"));
                Assert.That(exception.InnerException, Is.TypeOf<JsonException>());
            }
            finally
            {
                context.Shutdown();
            }
        }

        [Test]
        public void ReplaceState_IsCompleteSparseReplacement_NotMerge()
        {
            _system.ReplaceState(10, new List<PointAllocationNodeState>
            {
                new PointAllocationNodeState(1, 2),
                new PointAllocationNodeState(2, 1)
            }, 4);

            _system.ReplaceState(10, new List<PointAllocationNodeState>(), 7);

            Assert.That(_store.GetData().GraphStates, Has.Count.EqualTo(1));
            Assert.That(_store.GetData().GraphStates[0].GraphId, Is.EqualTo(10));
            Assert.That(_store.GetData().GraphStates[0].NodeStates, Is.Empty);
            Assert.That(_system.AvailablePoints, Is.EqualTo(7));
            Assert.That(_system.GetNodeLevel(10, 1), Is.Zero);
        }

        [Test]
        public void ReplaceState_InvalidInput_IsAtomic()
        {
            _system.ReplaceState(20, new List<PointAllocationNodeState>
            {
                new PointAllocationNodeState(1, 1)
            }, 5);

            Assert.Throws<ArgumentException>(() => _system.ReplaceState(
                10,
                new List<PointAllocationNodeState> { new PointAllocationNodeState(2, 1) },
                99));
            Assert.Throws<ArgumentNullException>(() => _system.ReplaceState(10, null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _system.ReplaceState(
                10,
                new List<PointAllocationNodeState>(),
                -1));

            Assert.That(_system.AvailablePoints, Is.EqualTo(5));
            Assert.That(_store.GetData().GraphStates, Has.Count.EqualTo(1));
            Assert.That(_store.GetData().GraphStates[0].GraphId, Is.EqualTo(20));
            Assert.That(_store.GetData().GraphStates[0].NodeStates, Has.Count.EqualTo(1));
        }

        [Test]
        public void ReplaceState_UsesProvidedList()
        {
            var nodeStates = new List<PointAllocationNodeState>
            {
                new PointAllocationNodeState(1, 1)
            };

            _system.ReplaceState(20, nodeStates, 2);

            Assert.That(_store.GetData().GraphStates[0].NodeStates, Is.SameAs(nodeStates));
        }

        [Test]
        public void Upgrade_UsesAndPrerequisitesAndSharedBalance()
        {
            _system.ReplaceState(10, new List<PointAllocationNodeState>
            {
                new PointAllocationNodeState(1, 1)
            }, 6);

            var blocked = _system.TryUpgrade(10, 2);
            Assert.That(blocked, Is.False);
            Assert.That(_system.AvailablePoints, Is.EqualTo(6));
            Assert.That(_system.GetNodeLevel(10, 1), Is.EqualTo(1));
            Assert.That(_system.GetNodeLevel(10, 2), Is.Zero);
            Assert.That(_system.CanUpgrade(10, 2), Is.False);
            Assert.That(_system.TryUpgrade(10, 1), Is.True);
            Assert.That(_system.CanUpgrade(10, 2), Is.True);
            Assert.That(_system.TryUpgrade(10, 2), Is.True);
            Assert.That(_system.TryUpgrade(20, 1), Is.True);

            Assert.That(_system.AvailablePoints, Is.Zero);
            Assert.That(_store.GetData().GraphStates, Has.Count.EqualTo(2));
            Assert.That(_store.GetData().GraphStates[0].NodeStates, Has.Count.EqualTo(2));
            Assert.That(_store.GetData().GraphStates[1].NodeStates, Has.Count.EqualTo(1));
        }

        [Test]
        public void Upgrade_NormalRuleRejectionReturnsFalseWithoutMutation()
        {
            _system.ReplaceState(20, new List<PointAllocationNodeState>(), 0);

            Assert.That(_system.CanUpgrade(20, 1), Is.False);
            Assert.That(_system.TryUpgrade(20, 1), Is.False);
            Assert.That(_system.AvailablePoints, Is.Zero);
            Assert.That(_store.GetData().GraphStates[0].NodeStates, Is.Empty);
        }

        [Test]
        public void ChangedEvent_PublishesOnlyAfterCommittedStateChanges()
        {
            var eventCount = 0;
            _context.Event.Subscribe<PointAllocationChangedEvent>(_ => eventCount++, this);

            _system.ReplaceState(20, new List<PointAllocationNodeState>(), 0);
            Assert.That(eventCount, Is.EqualTo(1));

            Assert.That(_system.TryUpgrade(20, 1), Is.False);
            Assert.That(eventCount, Is.EqualTo(1));

            _system.ReplaceState(20, new List<PointAllocationNodeState>(), 1);
            Assert.That(_system.TryUpgrade(20, 1), Is.True);
            Assert.That(eventCount, Is.EqualTo(3));

            _system.ResetGraph(20);
            Assert.That(eventCount, Is.EqualTo(4));

            _system.ResetGraph(20);
            Assert.That(eventCount, Is.EqualTo(4));
        }

        [Test]
        public void Commands_UnknownIdsThrowInsteadOfReturningBusinessFailure()
        {
            Assert.Throws<KeyNotFoundException>(() => _system.CanUpgrade(999, 1));
            Assert.Throws<KeyNotFoundException>(() => _system.CanUpgrade(10, 999));
            Assert.Throws<KeyNotFoundException>(() => _system.GetNodeLevel(999, 1));
            Assert.Throws<KeyNotFoundException>(() => _system.GetNodeLevel(10, 999));
            Assert.Throws<KeyNotFoundException>(() => _system.ResetGraph(999));
        }

        [Test]
        public void ResetGraph_ClearsOnlyThatGraphAndRefundsAllLevels()
        {
            _system.ReplaceState(10, new List<PointAllocationNodeState>
            {
                new PointAllocationNodeState(1, 2),
                new PointAllocationNodeState(2, 1)
            }, 0);
            _system.ReplaceState(20, new List<PointAllocationNodeState>
            {
                new PointAllocationNodeState(1, 1)
            }, 0);

            _system.ResetGraph(10);

            Assert.That(_system.AvailablePoints, Is.EqualTo(6));
            Assert.That(_store.GetData().GraphStates, Has.Count.EqualTo(1));
            Assert.That(_store.GetData().GraphStates[0].GraphId, Is.EqualTo(20));
            Assert.That(_system.GetNodeLevel(10, 1), Is.Zero);
            Assert.That(_system.GetNodeLevel(10, 2), Is.Zero);
            Assert.That(_store.GetData().GraphStates[0].NodeStates,
                Has.Some.Matches<PointAllocationNodeState>(
                    value => value.NodeId == 1 && value.Level == 1));
        }

        [Test]
        public void Initialization_UsesValidStoreDataRestoredBeforeSystem()
        {
            var context = new ArchContext();
            try
            {
                var store = new PointAllocationStore();
                store.ReplaceData(new PointAllocationStoreData
                {
                    AvailablePoints = 8,
                    GraphStates = new List<PointAllocationGraphState>
                    {
                        new PointAllocationGraphState(10, new List<PointAllocationNodeState>
                        {
                            new PointAllocationNodeState(1, 2),
                            new PointAllocationNodeState(2, 1)
                        })
                    }
                });
                context.RegisterStore(store);

                var system = new PointAllocationSystem();
                context.RegisterSystem(system);
                context.InitializeAsync().GetAwaiter().GetResult();
                system.LoadGraph(CreateGraph10Json());

                Assert.That(system.AvailablePoints, Is.EqualTo(8));
                Assert.That(system.GetNodeLevel(10, 2), Is.EqualTo(1));
            }
            finally
            {
                context.Shutdown();
            }
        }

        private static string CreateGraph10Json()
        {
            return JsonMapper.ToJson(new PointAllocationGraph(
                10,
                new[]
                {
                    new PointAllocationNode(1, 2, new[] { 1, 2 }),
                    new PointAllocationNode(2, 1, new[] { 3 })
                },
                new[] { new PointAllocationConnection(1, 2, 2) }));
        }

        private static string CreateGraph20Json()
        {
            return JsonMapper.ToJson(new PointAllocationGraph(
                20,
                new[] { new PointAllocationNode(1, 1, new[] { 1 }) },
                Array.Empty<PointAllocationConnection>()));
        }
    }
}
