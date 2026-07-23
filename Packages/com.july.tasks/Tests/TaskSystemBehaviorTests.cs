using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using July.Arch;
using July.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.Tasks.Tests.Task
{
    [TestFixture]
    public sealed class TaskSystemBehaviorTests
    {
        private ArchContext _context;
        private ITaskSystem _tasks;

        [SetUp]
        public void SetUp()
        {
            _context = new ArchContext();
            _context.RegisterSystem(new BehaviorTaskSystem());
            _context.InitializeAsync().GetAwaiter().GetResult();
            _tasks = _context.GetSystem<ITaskSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            _context?.Shutdown();
            _context = null;
            _tasks = null;
        }

        [Test]
        public void RegisterAndQuery_ReturnIndependentImmutableSnapshots()
        {
            var sourceStages = new[] { Stage(10, TaskState.Active) };
            var input = new TaskData(1, 2, sourceStages);
            sourceStages[0] = Stage(20, TaskState.Completed);

            Assert.That(_tasks.RegisterTask(input), Is.True);
            Assert.That(_tasks.TryGetTask(1, out var queried), Is.True);
            AssertTask(queried, 1, 2, 1);
            AssertStage(queried, 0, 10, TaskState.Active);

            var firstSnapshot = _tasks.GetAllTasks();
            var secondSnapshot = _tasks.GetAllTasks();
            Assert.That(firstSnapshot, Is.Not.SameAs(secondSnapshot));
            Assert.That(firstSnapshot.Count, Is.EqualTo(1));
        }

        [Test]
        public void RegisterDuplicate_FailsAndKeepsOriginal()
        {
            Assert.That(_tasks.RegisterTask(Task(1, 2, Stage(10, TaskState.Active))), Is.True);
            Assert.That(_tasks.RegisterTask(Task(1, 8, Stage(20, TaskState.Completed))), Is.False);
            Assert.That(_tasks.TryGetTask(1, out var task), Is.True);
            AssertTask(task, 1, 2, 1);
            AssertStage(task, 0, 10, TaskState.Active);
        }

        [Test]
        public void Register_InvalidStageStructure_Fails()
        {
            Assert.That(_tasks.RegisterTask(new TaskData(1, 0, Array.Empty<TaskStageData>())), Is.False);
            Assert.That(_tasks.RegisterTask(Task(2, 0, Stage(0, TaskState.Active))), Is.False);
        }

        [Test]
        public void SetCurrentValue_CompletesEveryReachedStage_AndPublishesValueFirst()
        {
            var order = new List<string>();
            TaskValueChangedEvent valueEvent = default;
            var stateEvents = new List<TaskStageStateChangedEvent>();
            _context.Event.Subscribe<TaskValueChangedEvent>(evt =>
            {
                valueEvent = evt;
                order.Add("Value");
            }, this);
            _context.Event.Subscribe<TaskStageStateChangedEvent>(evt =>
            {
                stateEvents.Add(evt);
                order.Add($"Stage:{evt.StageIndex}");
            }, this);
            _tasks.RegisterTask(Task(
                1,
                2,
                Stage(10, TaskState.Active),
                Stage(30, TaskState.Active),
                Stage(100, TaskState.Active)));

            Assert.That(_tasks.SetCurrentValue(1, 35), Is.True);

            Assert.That(_tasks.TryGetTask(1, out var task), Is.True);
            AssertTask(task, 1, 35, 3);
            AssertStage(task, 0, 10, TaskState.Completed);
            AssertStage(task, 1, 30, TaskState.Completed);
            AssertStage(task, 2, 100, TaskState.Active);
            Assert.That(order, Is.EqualTo(new[] { "Value", "Stage:0", "Stage:1" }));
            Assert.That(valueEvent.TaskId, Is.EqualTo(1));
            Assert.That(valueEvent.PreviousValue, Is.EqualTo(2));
            Assert.That(valueEvent.CurrentValue, Is.EqualTo(35));
            Assert.That(stateEvents, Has.Count.EqualTo(2));
            Assert.That(stateEvents[0].PreviousState, Is.EqualTo(TaskState.Active));
            Assert.That(stateEvents[0].CurrentState, Is.EqualTo(TaskState.Completed));
        }

        [TestCase(5)]
        [TestCase(4)]
        public void SetCurrentValue_SameOrLower_IsSuccessfulNoOp(long nextValue)
        {
            var eventCount = SubscribeChangeCount();
            _tasks.RegisterTask(Task(1, 5, Stage(10, TaskState.Active)));

            Assert.That(_tasks.SetCurrentValue(1, nextValue), Is.True);
            Assert.That(eventCount(), Is.Zero);
            Assert.That(_tasks.TryGetTask(1, out var task), Is.True);
            Assert.That(task.CurrentValue, Is.EqualTo(5));
        }

        [Test]
        public void SetCurrentValue_WithoutActiveStage_IsSuccessfulNoOp()
        {
            var eventCount = SubscribeChangeCount();
            _tasks.RegisterTask(Task(
                1,
                30,
                Stage(10, TaskState.Claimed),
                Stage(30, TaskState.Completed)));

            Assert.That(_tasks.SetCurrentValue(1, 50), Is.True);
            Assert.That(eventCount(), Is.Zero);
            Assert.That(_tasks.TryGetTask(1, out var task), Is.True);
            Assert.That(task.CurrentValue, Is.EqualTo(30));
        }

        [Test]
        public void ClaimStage_CanSkipEarlierStage_AndIsIdempotent()
        {
            var stateEvents = new List<TaskStageStateChangedEvent>();
            _context.Event.Subscribe<TaskStageStateChangedEvent>(stateEvents.Add, this);
            _tasks.RegisterTask(Task(
                1,
                35,
                Stage(10, TaskState.Completed),
                Stage(30, TaskState.Completed),
                Stage(100, TaskState.Active)));

            Assert.That(_tasks.ClaimStage(1, 1), Is.True);
            Assert.That(_tasks.ClaimStage(1, 1), Is.True);
            Assert.That(_tasks.ClaimStage(1, 2), Is.False);
            Assert.That(_tasks.ClaimStage(1, -1), Is.False);
            Assert.That(_tasks.ClaimStage(1, 3), Is.False);

            Assert.That(_tasks.TryGetTask(1, out var task), Is.True);
            AssertStage(task, 0, 10, TaskState.Completed);
            AssertStage(task, 1, 30, TaskState.Claimed);
            Assert.That(stateEvents, Has.Count.EqualTo(1));
            Assert.That(stateEvents[0].StageIndex, Is.EqualTo(1));
            Assert.That(stateEvents[0].PreviousState, Is.EqualTo(TaskState.Completed));
            Assert.That(stateEvents[0].CurrentState, Is.EqualTo(TaskState.Claimed));
        }

        [Test]
        public void ResetTask_ClearsValueThenEveryStageState_AndKeepsTargets()
        {
            var order = new List<string>();
            _context.Event.Subscribe<TaskValueChangedEvent>(_ => order.Add("Value"), this);
            _context.Event.Subscribe<TaskStageStateChangedEvent>(
                evt => order.Add($"Stage:{evt.StageIndex}"),
                this);
            _tasks.RegisterTask(Task(
                1,
                35,
                Stage(10, TaskState.Claimed),
                Stage(30, TaskState.Completed),
                Stage(100, TaskState.Active)));

            Assert.That(_tasks.ResetTask(1), Is.True);

            Assert.That(_tasks.TryGetTask(1, out var task), Is.True);
            AssertTask(task, 1, 0, 3);
            AssertStage(task, 0, 10, TaskState.Active);
            AssertStage(task, 1, 30, TaskState.Active);
            AssertStage(task, 2, 100, TaskState.Active);
            Assert.That(order, Is.EqualTo(new[] { "Value", "Stage:0", "Stage:1" }));
        }

        [Test]
        public void ResetAllTasks_ResetsAtomically_AndOnlyPublishesActualChanges()
        {
            var events = new List<string>();
            var allResetWhenPublishing = true;
            _context.Event.Subscribe<TaskValueChangedEvent>(evt =>
            {
                events.Add($"Value:{evt.TaskId}");
                allResetWhenPublishing &= AreAllTasksReset();
            }, this);
            _context.Event.Subscribe<TaskStageStateChangedEvent>(evt =>
            {
                events.Add($"Stage:{evt.TaskId}:{evt.StageIndex}");
                allResetWhenPublishing &= AreAllTasksReset();
            }, this);
            _tasks.RegisterTask(Task(1, 5, Stage(10, TaskState.Active)));
            _tasks.RegisterTask(Task(
                2,
                30,
                Stage(10, TaskState.Claimed),
                Stage(30, TaskState.Completed)));
            _tasks.RegisterTask(Task(3, 0, Stage(10, TaskState.Active)));

            Assert.That(_tasks.ResetAllTasks(), Is.True);

            Assert.That(allResetWhenPublishing, Is.True);
            Assert.That(events, Has.Count.EqualTo(4));
            Assert.That(events.FindAll(item => item == "Value:1"), Has.Count.EqualTo(1));
            Assert.That(events.FindAll(item => item == "Value:2"), Has.Count.EqualTo(1));
            Assert.That(events.FindAll(item => item.StartsWith("Stage:2:")), Has.Count.EqualTo(2));
            Assert.That(events.FindAll(item => item.Contains(":3")), Is.Empty);

            events.Clear();
            Assert.That(_tasks.ResetAllTasks(), Is.True);
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void ReplaceAllTasks_InvalidItem_IsAtomic()
        {
            var replacedCount = 0;
            _context.Event.Subscribe<TaskCollectionReplacedEvent>(_ => replacedCount++, this);
            _tasks.RegisterTask(Task(1, 3, Stage(10, TaskState.Active)));
            Assert.That(_tasks.ReplaceAllTasks(new[]
            {
                Task(2, 5, Stage(20, TaskState.Active)),
                Task(3, -1, Stage(20, TaskState.Active))
            }), Is.False);

            Assert.That(replacedCount, Is.Zero);
            Assert.That(_tasks.TryGetTask(1, out var original), Is.True);
            Assert.That(original.CurrentValue, Is.EqualTo(3));
            Assert.That(_tasks.TryGetTask(2, out _), Is.False);
        }

        [Test]
        public void ReplaceAllTasks_PreservesAuthoritativeInconsistency_AndPublishesOnlyMarker()
        {
            var valueCount = 0;
            var stateCount = 0;
            var replacedCount = 0;
            _context.Event.Subscribe<TaskValueChangedEvent>(_ => valueCount++, this);
            _context.Event.Subscribe<TaskStageStateChangedEvent>(_ => stateCount++, this);
            _context.Event.Subscribe<TaskCollectionReplacedEvent>(_ => replacedCount++, this);
            _tasks.RegisterTask(Task(1, 0, Stage(10, TaskState.Active)));

            Assert.That(_tasks.ReplaceAllTasks(new[]
            {
                Task(2, 120, Stage(100, TaskState.Active)),
                Task(3, 50, Stage(100, TaskState.Completed))
            }), Is.True);

            Assert.That(_tasks.TryGetTask(1, out _), Is.False);
            Assert.That(_tasks.TryGetTask(2, out var activeAboveTarget), Is.True);
            AssertStage(activeAboveTarget, 0, 100, TaskState.Active);
            Assert.That(_tasks.TryGetTask(3, out var completedBelowTarget), Is.True);
            AssertStage(completedBelowTarget, 0, 100, TaskState.Completed);
            Assert.That(valueCount, Is.Zero);
            Assert.That(stateCount, Is.Zero);
            Assert.That(replacedCount, Is.EqualTo(1));
        }

        [Test]
        public void ReplaceAllTasks_IdenticalOrEmpty_StillPublishesOneMarkerPerSuccess()
        {
            var replacedCount = 0;
            _context.Event.Subscribe<TaskCollectionReplacedEvent>(_ => replacedCount++, this);
            var tasks = new[] { Task(1, 0, Stage(10, TaskState.Active)) };

            Assert.That(_tasks.ReplaceAllTasks(tasks), Is.True);
            Assert.That(_tasks.ReplaceAllTasks(tasks), Is.True);
            Assert.That(_tasks.ReplaceAllTasks(Array.Empty<TaskData>()), Is.True);

            Assert.That(replacedCount, Is.EqualTo(3));
            Assert.That(_tasks.GetAllTasks(), Is.Empty);
        }

        [Test]
        public void ReplaceTasks_ReplacesOnlySpecifiedTasks_BeforePublishingMarkers()
        {
            var replacedTaskIds = new List<int>();
            var allTasksReplacedWhenPublishing = true;
            var valueCount = 0;
            var stateCount = 0;
            var collectionCount = 0;
            _context.Event.Subscribe<TaskReplacedEvent>(evt =>
            {
                replacedTaskIds.Add(evt.TaskId);
                allTasksReplacedWhenPublishing &=
                    _tasks.TryGetTask(1, out var first) && first.CurrentValue == 15 &&
                    _tasks.TryGetTask(2, out var second) && second.CurrentValue == 25;
            }, this);
            _context.Event.Subscribe<TaskValueChangedEvent>(_ => valueCount++, this);
            _context.Event.Subscribe<TaskStageStateChangedEvent>(_ => stateCount++, this);
            _context.Event.Subscribe<TaskCollectionReplacedEvent>(_ => collectionCount++, this);
            _tasks.RegisterTask(Task(1, 0, Stage(10, TaskState.Active)));
            _tasks.RegisterTask(Task(2, 0, Stage(20, TaskState.Active)));
            _tasks.RegisterTask(Task(3, 3, Stage(30, TaskState.Active)));

            Assert.That(_tasks.ReplaceTasks(new[]
            {
                Task(1, 15, Stage(10, TaskState.Active)),
                Task(2, 25, Stage(20, TaskState.Completed))
            }), Is.True);

            Assert.That(allTasksReplacedWhenPublishing, Is.True);
            Assert.That(replacedTaskIds, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(valueCount, Is.Zero);
            Assert.That(stateCount, Is.Zero);
            Assert.That(collectionCount, Is.Zero);
            Assert.That(_tasks.TryGetTask(3, out var untouched), Is.True);
            Assert.That(untouched.CurrentValue, Is.EqualTo(3));
        }

        [Test]
        public void ReplaceTasks_InvalidUnknownOrDuplicateItem_IsAtomic()
        {
            var replacedCount = 0;
            _context.Event.Subscribe<TaskReplacedEvent>(_ => replacedCount++, this);
            _tasks.RegisterTask(Task(1, 1, Stage(10, TaskState.Active)));
            _tasks.RegisterTask(Task(2, 2, Stage(20, TaskState.Active)));

            Assert.That(_tasks.ReplaceTasks(new[]
            {
                Task(1, 10, Stage(10, TaskState.Completed)),
                Task(3, 30, Stage(30, TaskState.Completed))
            }), Is.False);
            Assert.That(_tasks.ReplaceTasks(new[]
            {
                Task(1, 10, Stage(10, TaskState.Completed)),
                Task(1, 11, Stage(10, TaskState.Completed))
            }), Is.False);
            Assert.That(_tasks.ReplaceTasks(new[]
            {
                Task(1, 10, Stage(10, TaskState.Completed)),
                Task(2, -1, Stage(20, TaskState.Active))
            }), Is.False);

            Assert.That(replacedCount, Is.Zero);
            Assert.That(_tasks.TryGetTask(1, out var first), Is.True);
            Assert.That(_tasks.TryGetTask(2, out var second), Is.True);
            Assert.That(first.CurrentValue, Is.EqualTo(1));
            Assert.That(second.CurrentValue, Is.EqualTo(2));
        }

        [Test]
        public void ReplaceTasks_Empty_IsSuccessfulNoOp()
        {
            var replacedCount = 0;
            _context.Event.Subscribe<TaskReplacedEvent>(_ => replacedCount++, this);
            _tasks.RegisterTask(Task(1, 1, Stage(10, TaskState.Active)));

            Assert.That(_tasks.ReplaceTasks(Array.Empty<TaskData>()), Is.True);

            Assert.That(replacedCount, Is.Zero);
            Assert.That(_tasks.TryGetTask(1, out var task), Is.True);
            Assert.That(task.CurrentValue, Is.EqualTo(1));
        }

        [Test]
        public void ListenerException_IsLoggedAndDoesNotStopFollowingEvents()
        {
            var stateEventCount = 0;
            _context.Event.Subscribe<TaskValueChangedEvent>(_ =>
            {
                throw new InvalidOperationException("listener failure");
            }, this);
            _context.Event.Subscribe<TaskStageStateChangedEvent>(_ => stateEventCount++, this);
            _tasks.RegisterTask(Task(1, 0, Stage(1, TaskState.Active)));
            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: listener failure"));

            Assert.DoesNotThrow(() => _tasks.SetCurrentValue(1, 1));
            Assert.That(stateEventCount, Is.EqualTo(1));
        }

        [Test]
        public void RemoveTask_IsSilent()
        {
            var eventCount = SubscribeChangeCount();
            _tasks.RegisterTask(Task(1, 0, Stage(1, TaskState.Active)));

            Assert.That(_tasks.RemoveTask(1), Is.True);
            Assert.That(_tasks.TryGetTask(1, out _), Is.False);
            Assert.That(eventCount(), Is.Zero);
        }

        private Func<int> SubscribeChangeCount()
        {
            var eventCount = 0;
            _context.Event.Subscribe<TaskValueChangedEvent>(_ => eventCount++, this);
            _context.Event.Subscribe<TaskStageStateChangedEvent>(_ => eventCount++, this);
            return () => eventCount;
        }

        private bool AreAllTasksReset()
        {
            var snapshot = _tasks.GetAllTasks();
            for (var taskIndex = 0; taskIndex < snapshot.Count; taskIndex++)
            {
                var task = snapshot[taskIndex];
                if (task.CurrentValue != 0)
                    return false;

                for (var stageIndex = 0; stageIndex < task.Stages.Count; stageIndex++)
                {
                    if (task.Stages[stageIndex].State != TaskState.Active)
                        return false;
                }
            }

            return true;
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

        private static void AssertTask(
            TaskData task,
            int taskId,
            long currentValue,
            int stageCount)
        {
            Assert.That(task.TaskId, Is.EqualTo(taskId));
            Assert.That(task.CurrentValue, Is.EqualTo(currentValue));
            Assert.That(task.Stages, Has.Count.EqualTo(stageCount));
        }

        private static void AssertStage(
            TaskData task,
            int stageIndex,
            long targetValue,
            TaskState state)
        {
            Assert.That(task.Stages[stageIndex].TargetValue, Is.EqualTo(targetValue));
            Assert.That(task.Stages[stageIndex].State, Is.EqualTo(state));
        }

        private sealed class BehaviorTaskSystem : TaskSystemBase
        {
        }
    }
}
