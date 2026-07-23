using System.Collections.Generic;
using July.Arch;
using July.Tasks;
using NUnit.Framework;

namespace July.Tasks.Tests.Task
{
    [TestFixture]
    public sealed class TaskSystemContractTests
    {
        private ArchContext _context;
        private TestTaskSystem _system;

        [SetUp]
        public void SetUp()
        {
            _context = new ArchContext();
            _system = new TestTaskSystem();
            _context.RegisterSystem(_system);
            _context.InitializeAsync().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _context?.Shutdown();
            _context = null;
            _system = null;
        }

        [Test]
        public void OneRegisteredInstance_ResolvesByInterfaceBaseAndConcreteType()
        {
            Assert.That(_system.ConfigureCalled, Is.True);
            Assert.That(_context.GetSystem<ITaskSystem>(), Is.SameAs(_system));
            Assert.That(_context.GetSystem<TaskSystemBase>(), Is.SameAs(_system));
            Assert.That(_context.GetSystem<TestTaskSystem>(), Is.SameAs(_system));
        }

        [Test]
        public void PublicEvents_ExposeConfirmedPayloads()
        {
            var valueEvents = new List<TaskValueChangedEvent>();
            var stateEvents = new List<TaskStageStateChangedEvent>();
            _context.Event.Subscribe<TaskValueChangedEvent>(valueEvents.Add, this);
            _context.Event.Subscribe<TaskStageStateChangedEvent>(stateEvents.Add, this);
            _system.RegisterTask(Task(
                1,
                2,
                Stage(10, TaskState.Active),
                Stage(30, TaskState.Active)));

            Assert.That(_system.SetCurrentValue(1, 15), Is.True);

            Assert.That(valueEvents, Has.Count.EqualTo(1));
            Assert.That(valueEvents[0].TaskId, Is.EqualTo(1));
            Assert.That(valueEvents[0].PreviousValue, Is.EqualTo(2));
            Assert.That(valueEvents[0].CurrentValue, Is.EqualTo(15));
            Assert.That(stateEvents, Has.Count.EqualTo(1));
            Assert.That(stateEvents[0].TaskId, Is.EqualTo(1));
            Assert.That(stateEvents[0].StageIndex, Is.Zero);
            Assert.That(stateEvents[0].PreviousState, Is.EqualTo(TaskState.Active));
            Assert.That(stateEvents[0].CurrentState, Is.EqualTo(TaskState.Completed));
        }

        [Test]
        public void NestedPublicCommand_PreservesRootCommandEventOrder()
        {
            var order = new List<string>();
            _system.RegisterTask(Task(1, 0, Stage(1, TaskState.Active)));
            _system.RegisterTask(Task(2, 0, Stage(1, TaskState.Active)));
            _context.Event.Subscribe<TaskValueChangedEvent>(valueEvent =>
            {
                order.Add($"Value:{valueEvent.TaskId}");
                if (valueEvent.TaskId == 1)
                    _system.SetCurrentValue(2, 1);
            }, this);
            _context.Event.Subscribe<TaskStageStateChangedEvent>(stateEvent =>
            {
                order.Add($"Stage:{stateEvent.TaskId}:{stateEvent.StageIndex}");
            }, this);

            Assert.That(_system.SetCurrentValue(1, 1), Is.True);

            Assert.That(order, Is.EqualTo(new[]
            {
                "Value:1",
                "Stage:1:0",
                "Value:2",
                "Stage:2:0"
            }));
        }

        [Test]
        public void FullReplacement_PublishesOnlyOneMarker()
        {
            var valueCount = 0;
            var stateCount = 0;
            var replacedCount = 0;
            _context.Event.Subscribe<TaskValueChangedEvent>(_ => valueCount++, this);
            _context.Event.Subscribe<TaskStageStateChangedEvent>(_ => stateCount++, this);
            _context.Event.Subscribe<TaskCollectionReplacedEvent>(_ => replacedCount++, this);

            Assert.That(_system.ReplaceAllTasks(new[]
            {
                Task(1, 20, Stage(10, TaskState.Active)),
                Task(2, 1, Stage(10, TaskState.Completed))
            }), Is.True);

            Assert.That(valueCount, Is.Zero);
            Assert.That(stateCount, Is.Zero);
            Assert.That(replacedCount, Is.EqualTo(1));
        }

        [Test]
        public void PartialReplacement_IsAvailableThroughInterface_AndPublishesTaskMarkers()
        {
            var taskSystem = _context.GetSystem<ITaskSystem>();
            var replacedTaskIds = new List<int>();
            _context.Event.Subscribe<TaskReplacedEvent>(
                evt => replacedTaskIds.Add(evt.TaskId),
                this);
            taskSystem.RegisterTask(Task(1, 0, Stage(10, TaskState.Active)));
            taskSystem.RegisterTask(Task(2, 0, Stage(20, TaskState.Active)));

            Assert.That(taskSystem.ReplaceTasks(new[]
            {
                Task(2, 20, Stage(20, TaskState.Completed)),
                Task(1, 10, Stage(10, TaskState.Completed))
            }), Is.True);

            Assert.That(replacedTaskIds, Is.EqualTo(new[] { 2, 1 }));
            Assert.That(taskSystem.TryGetTask(1, out var first), Is.True);
            Assert.That(taskSystem.TryGetTask(2, out var second), Is.True);
            Assert.That(first.CurrentValue, Is.EqualTo(10));
            Assert.That(second.CurrentValue, Is.EqualTo(20));
        }

        [Test]
        public void ResetAllTasks_IsAvailableThroughTaskInterface()
        {
            var taskSystem = _context.GetSystem<ITaskSystem>();
            taskSystem.RegisterTask(Task(1, 5, Stage(10, TaskState.Active)));
            taskSystem.RegisterTask(Task(
                2,
                30,
                Stage(10, TaskState.Claimed),
                Stage(30, TaskState.Completed)));

            Assert.That(taskSystem.ResetAllTasks(), Is.True);
            Assert.That(taskSystem.TryGetTask(1, out var first), Is.True);
            Assert.That(taskSystem.TryGetTask(2, out var second), Is.True);
            Assert.That(first.CurrentValue, Is.Zero);
            Assert.That(first.Stages[0].State, Is.EqualTo(TaskState.Active));
            Assert.That(second.CurrentValue, Is.Zero);
            Assert.That(second.Stages[0].State, Is.EqualTo(TaskState.Active));
            Assert.That(second.Stages[1].State, Is.EqualTo(TaskState.Active));
        }

        [Test]
        public void Shutdown_InvokesDisposeBeforeClearingCore()
        {
            _system.RegisterTask(Task(1, 1, Stage(10, TaskState.Active)));

            _context.Shutdown();
            _context = null;

            Assert.That(_system.DisposeCalled, Is.True);
            Assert.That(_system.TaskWasVisibleDuringDispose, Is.True);
            Assert.That(_system.TryGetTask(1, out _), Is.False);
            Assert.That(_system.GetAllTasks(), Is.Empty);
        }

        private static TaskData Task(
            int taskId,
            long currentValue,
            params TaskStageData[] stages)
        {
            return new TaskData(taskId, currentValue, stages);
        }

        private static TaskStageData Stage(long targetValue, TaskState state)
        {
            return new TaskStageData(targetValue, state);
        }

        private sealed class TestTaskSystem : TaskSystemBase
        {
            public bool ConfigureCalled { get; private set; }
            public bool DisposeCalled { get; private set; }
            public bool TaskWasVisibleDuringDispose { get; private set; }

            protected override void OnConfigure()
            {
                ConfigureCalled = true;
            }

            protected override void OnDispose()
            {
                DisposeCalled = true;
                TaskWasVisibleDuringDispose = TryGetTask(1, out _);
            }
        }
    }
}
