using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Events;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.Guide.Validation
{
    /// <summary>
    /// 验证 Guide/Arch 公开契约的集成测试。教学操作由普通的项目侧 Procedure 实现；
    /// 取消、执行顺序、通知和保存均使用正式实现。
    /// </summary>
    public sealed class GuideLifecycleTests
    {
        private const int FirstGuide = 7101;
        private const int SecondGuide = 7102;
        private const int DefaultConditionType = 51;
        private ArchContext architecture;
        private GuideStore store;
        private IGuideSystem guide;
        private Action<Exception> previousEventErrorHandler;
        private readonly List<Action> releaseCleanup = new();

        [SetUp]
        public void SetUp()
        {
            previousEventErrorHandler = EventBus.ErrorHandler;
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                // 断言失败时，也必须释放测试中故意暂停的 Procedure finally 清理流程。
                foreach (var release in releaseCleanup) release();
                if (guide != null && guide.IsRunning)
                {
                    var stop = guide.StopAsync();
                    Assert.That(stop.Status, Is.Not.EqualTo(UniTaskStatus.Pending),
                        "A test-owned cleanup permit was not released.");
                    stop.GetAwaiter().GetResult();
                }
            }
            finally
            {
                architecture?.Shutdown();
                architecture = null;
                store = null;
                guide = null;
                releaseCleanup.Clear();
                EventBus.ErrorHandler = previousEventErrorHandler;
            }
        }

        [Test]
        public void StartConditions_AreRegisteredOnce_AndReadLatestStateOnEachTrigger()
        {
            var allowed = false;
            var condition = new TestStartCondition { Evaluate = _ => allowed };
            var registrations = 0;
            var executions = 0;
            IEnumerable<IGuideStartCondition> Conditions()
            {
                registrations++;
                yield return condition;
            }
            Initialize(new[] { Definition(FirstGuide, 1) }, (_, __) =>
            {
                executions++;
                return UniTask.CompletedTask;
            }, Conditions());

            Assert.That(registrations, Is.EqualTo(1));
            Assert.That(condition.EvaluatedParams, Is.Empty, "初始化只注册策略，不评估业务状态。");
            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.Null);
            Assert.That(executions, Is.Zero);

            allowed = true;
            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
            Assert.That(executions, Is.EqualTo(1));
            Assert.That(condition.EvaluatedParams.Count, Is.EqualTo(2));
            Assert.That(registrations, Is.EqualTo(1));
            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.Null);
            Assert.That(condition.EvaluatedParams.Count, Is.EqualTo(2), "已完成计划不再评估条件。");
        }

        [Test]
        public void StartConditions_DispatchByType_WithTheSameParameterId()
        {
            const int secondType = 52;
            const int paramId = 201;
            var firstAllowed = false;
            var firstCondition = new TestStartCondition { Evaluate = _ => firstAllowed };
            var secondCondition = new TestStartCondition(secondType);
            var started = new List<int>();
            Initialize(new[]
            {
                new GuidePlan(FirstGuide, new[] { new GuideStep(1, 9001) },
                    DefaultConditionType, paramId, priority: 10),
                new GuidePlan(SecondGuide, new[] { new GuideStep(1, 9001) }, secondType, paramId)
            }, (_, __) => UniTask.CompletedTask, new[] { firstCondition, secondCondition });
            architecture.Event.Subscribe<GuideStartedEvent>(e => started.Add(e.GuideId), this);

            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
            CollectionAssert.AreEqual(new[] { SecondGuide }, started);
            Assert.That(store.IsFinished(FirstGuide), Is.False);
            CollectionAssert.AreEqual(new[] { paramId }, firstCondition.EvaluatedParams);
            CollectionAssert.AreEqual(new[] { paramId }, secondCondition.EvaluatedParams);

            firstAllowed = true;
            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
            CollectionAssert.AreEqual(new[] { SecondGuide, FirstGuide }, started);
        }

        [Test]
        public void StartConditions_ReuseOneStrategy_WithDifferentParameterRecords()
        {
            var parameters = new Dictionary<int, bool> { [201] = false, [202] = true };
            var condition = new TestStartCondition { Evaluate = id => parameters[id] };
            var started = new List<int>();
            Initialize(new[]
            {
                new GuidePlan(FirstGuide, new[] { new GuideStep(1, 9001) },
                    DefaultConditionType, 201, priority: 10),
                new GuidePlan(SecondGuide, new[] { new GuideStep(1, 9001) }, DefaultConditionType, 202)
            }, (_, __) => UniTask.CompletedTask, new[] { condition });
            architecture.Event.Subscribe<GuideStartedEvent>(e => started.Add(e.GuideId), this);

            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
            CollectionAssert.AreEqual(new[] { SecondGuide }, started);
            CollectionAssert.AreEquivalent(new[] { 201, 202 }, condition.EvaluatedParams);

            parameters[201] = true;
            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
            CollectionAssert.AreEqual(new[] { SecondGuide, FirstGuide }, started);
            Assert.That(condition.EvaluatedParams.Count, Is.EqualTo(3));
        }

        [Test]
        public void StartConditions_RejectDuplicateTypesDuringInitialization()
        {
            var error = Assert.Throws<InvalidOperationException>(() => Initialize(
                new[] { Definition(FirstGuide, 1) }, (_, __) => UniTask.CompletedTask,
                new[] { new TestStartCondition(), new TestStartCondition() }));
            StringAssert.Contains(DefaultConditionType.ToString(), error.Message);
        }

        [Test]
        public void StartConditions_RejectUnregisteredTypeDuringInitialization()
        {
            var error = Assert.Throws<InvalidOperationException>(() => Initialize(
                new[] { Definition(FirstGuide, 1) }, (_, __) => UniTask.CompletedTask,
                Array.Empty<IGuideStartCondition>()));
            StringAssert.Contains(FirstGuide.ToString(), error.Message);
            StringAssert.Contains(DefaultConditionType.ToString(), error.Message);
        }

        [Test]
        public void StepsRunInOrder_AndCompletionIsSavedAfterCleanup()
        {
            var trace = new List<string>();
            var firstWork = new UniTaskCompletionSource();
            Initialize(new[] { Definition(FirstGuide, 1, 2, 3) }, (context, ct) =>
                ExecuteStepAsync(context.Step.Id, firstWork, trace, ct));
            architecture.Event.Subscribe<GuideStepEnteredEvent>(
                e => trace.Add($"enter:{e.StepId}"), this);
            architecture.Event.Subscribe<GuideStepExitedEvent>(
                e => trace.Add($"exit:{e.StepId}"), this);
            store.DirtyMarked += () => trace.Add("saved");

            var run = guide.RunAsync();
            CollectionAssert.AreEqual(new[] { "enter:1", "work:1" }, trace);
            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(store.IsFinished(FirstGuide), Is.False);

            firstWork.TrySetResult();

            Assert.That(run.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
            CollectionAssert.AreEqual(new[]
            {
                "enter:1", "work:1", "cleanup:1", "exit:1",
                "enter:2", "work:2", "cleanup:2", "exit:2",
                "enter:3", "work:3", "cleanup:3", "exit:3", "saved"
            }, trace);
            Assert.That(guide.IsRunning, Is.False);
            Assert.That(guide.CurrentStepId, Is.Zero);
            Assert.That(RestoreSavedOutcomes().IsCompleted(FirstGuide), Is.True);
            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.Null,
                "A completed guide must not be selected again.");
        }

        [UnityTest]
        public IEnumerator StopWaitsForActualAsyncFinally_AndAbortIsNotSaved()
        {
            var operation = NewControlledOperation();
            Initialize(new[] { Definition(FirstGuide, 1) }, (_, ct) => operation.ExecuteAsync(ct));

            var run = guide.RunAsync();
            var stop = guide.StopAsync();

            Assert.That(operation.FinallyEntered, Is.True);
            Assert.That(operation.FinallyCompleted, Is.False);
            Assert.That(stop.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Pending));
            yield return null;
            Assert.That(stop.Status, Is.EqualTo(UniTaskStatus.Pending),
                "Cancellation is not completion while the Procedure still owns cleanup.");

            operation.AllowCleanup.TrySetResult();

            stop.GetAwaiter().GetResult();
            Assert.That(operation.FinallyCompleted, Is.True);
            Assert.That(run.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Aborted));
            AssertNoSavedOutcome(FirstGuide);
        }

        [Test]
        public void Stop_WaitsForCleanup_AndCannotBecomeSkipped()
        {
            var operation = NewControlledOperation();
            Initialize(new[] { Definition(FirstGuide, 1) }, (_, ct) => operation.ExecuteAsync(ct));
            var run = guide.RunAsync();
            var stop = guide.StopAsync();
            Assert.That(stop.Status, Is.EqualTo(UniTaskStatus.Pending));

            Assert.That(operation.FinallyEntered, Is.True);
            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Pending));
            var skip = guide.SkipCurrentGuideAsync();
            Assert.That(skip.Status, Is.EqualTo(UniTaskStatus.Pending));
            operation.AllowCleanup.TrySetResult();

            Assert.That(run.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Aborted));
            stop.GetAwaiter().GetResult();
            Assert.That(skip.GetAwaiter().GetResult(), Is.False);
            Assert.That(operation.FinallyCompleted, Is.True);
            Assert.That(guide.IsRunning, Is.False);
            AssertNoSavedOutcome(FirstGuide);
        }

        [Test]
        public void RepeatedStops_WaitForCurrentCleanup_AndBusyRunIsIgnored()
        {
            var operation = NewControlledOperation();
            Initialize(new[] { Definition(FirstGuide, 1) }, (_, ct) => operation.ExecuteAsync(ct));
            var run = guide.RunAsync();
            var firstStop = guide.StopAsync();
            var secondStop = guide.StopAsync();

            Assert.That(firstStop.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(secondStop.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.Null);
            operation.AllowCleanup.TrySetResult();

            firstStop.GetAwaiter().GetResult();
            secondStop.GetAwaiter().GetResult();
            Assert.That(run.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Aborted));
            Assert.That(guide.IsRunning, Is.False);
            AssertNoSavedOutcome(FirstGuide);
        }

        [Test]
        public void TerminalListenerRun_IsIgnored_AndNextGuideNeedsExplicitLaterTrigger()
        {
            var started = new List<int>();
            var ignored = default(UniTask<GuideExitReason?>);
            var triggeredInTerminal = false;
            var runningDuringTerminal = false;
            var guideIdDuringTerminal = 0;
            Initialize(new[] { Definition(FirstGuide, 1), Definition(SecondGuide, 1) },
                (_, __) => UniTask.CompletedTask);
            architecture.Event.Subscribe<GuideStartedEvent>(e => started.Add(e.GuideId), this);
            architecture.Event.Subscribe<GuideExitedEvent>(e =>
            {
                if (e.GuideId != FirstGuide) return;
                runningDuringTerminal = guide.IsRunning;
                guideIdDuringTerminal = guide.CurrentGuideId;
                ignored = guide.RunAsync();
                Assert.That(ignored.Status, Is.EqualTo(UniTaskStatus.Succeeded));
                triggeredInTerminal = true;
            }, this);

            var first = guide.RunAsync();

            Assert.That(first.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
            Assert.That(triggeredInTerminal, Is.True);
            Assert.That(runningDuringTerminal, Is.True);
            Assert.That(guideIdDuringTerminal, Is.EqualTo(FirstGuide));
            Assert.That(ignored.GetAwaiter().GetResult(), Is.Null);
            CollectionAssert.AreEqual(new[] { FirstGuide }, started);
            Assert.That(store.IsFinished(SecondGuide), Is.False);

            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
            CollectionAssert.AreEqual(new[] { FirstGuide, SecondGuide }, started);
        }

        [Test]
        public void ExplicitSkip_WaitsForCleanup_AndPersistsOnlySkippedOutcome()
        {
            var operation = NewControlledOperation();
            Initialize(new[] { Definition(FirstGuide, 1) }, (_, ct) => operation.ExecuteAsync(ct));
            var run = guide.RunAsync();

            var skip = guide.SkipCurrentGuideAsync();

            Assert.That(operation.FinallyEntered, Is.True);
            Assert.That(skip.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(store.IsFinished(FirstGuide), Is.False);
            operation.AllowCleanup.TrySetResult();

            Assert.That(skip.GetAwaiter().GetResult(), Is.True);
            Assert.That(run.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Skipped));
            var restored = RestoreSavedOutcomes();
            Assert.That(restored.IsSkipped(FirstGuide), Is.True);
            Assert.That(restored.IsCompleted(FirstGuide), Is.False);
            CollectionAssert.IsEmpty(store.GetData().CompletedGuideIds);
            CollectionAssert.AreEqual(new[] { FirstGuide }, store.GetData().SkippedGuideIds);
            Assert.That(guide.RunAsync().GetAwaiter().GetResult(), Is.Null);
        }

        [Test]
        public void TeachingFailure_IsReportedAfterCleanup_AndNeverSaved()
        {
            var teachingFailure = new InvalidOperationException("teaching-failed");
            var cleanedUp = false;
            GuideExitedEvent? terminal = null;
            Initialize(new[] { Definition(FirstGuide, 1) }, (_, __) =>
                FailTeachingAsync(teachingFailure, () => cleanedUp = true));
            architecture.Event.Subscribe<GuideExitedEvent>(e => terminal = e, this);

            var run = guide.RunAsync();

            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.That(ReadFailure(run), Is.SameAs(teachingFailure));
            Assert.That(cleanedUp, Is.True);
            Assert.That(terminal.HasValue, Is.True);
            Assert.That(terminal.Value.Reason, Is.EqualTo(GuideExitReason.Faulted));
            Assert.That(terminal.Value.Error, Is.SameAs(teachingFailure));
            Assert.That(guide.LastFailure, Is.SameAs(teachingFailure));
            Assert.That(guide.IsRunning, Is.False);
            AssertNoSavedOutcome(FirstGuide);
        }

        [Test]
        public void TerminalListenerFailure_IsReportedByEventBus_WithoutChangingOutcome()
        {
            var listenerFailure = new InvalidOperationException("terminal-listener");
            Exception reported = null;
            Initialize(new[] { Definition(FirstGuide, 1) }, (_, __) => UniTask.CompletedTask);
            architecture.Event.Subscribe<GuideExitedEvent>(_ => throw listenerFailure, this);
            EventBus.ErrorHandler = error => reported = error;

            var run = guide.RunAsync();

            Assert.That(run.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
            Assert.That(reported, Is.SameAs(listenerFailure));
            Assert.That(guide.LastFailure, Is.Null);
            Assert.That(guide.IsRunning, Is.False);
            Assert.That(RestoreSavedOutcomes().IsCompleted(FirstGuide), Is.True);
            Assert.That(guide.StopAsync().Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }

        [Test]
        public void PreviousStepContext_CannotChangeCurrentStep()
        {
            GuideStepContext previous = null;
            var first = new UniTaskCompletionSource();
            var second = new UniTaskCompletionSource();
            Initialize(new[] { Definition(FirstGuide, 1, 2) }, (context, ct) =>
            {
                context.SetWaitingFor($"step:{context.Step.Id}");
                if (context.Step.Id == 1) previous = context;
                return (context.Step.Id == 1 ? first : second).Task.AttachExternalCancellation(ct);
            });
            var run = guide.RunAsync();
            first.TrySetResult();
            previous.RequestSkip();
            Assert.Throws<InvalidOperationException>(() => previous.SetWaitingFor("旧等待"));
            Assert.That(guide.CurrentStepId, Is.EqualTo(2));
            Assert.That(guide.WaitingFor, Is.EqualTo("step:2"));
            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Pending));
            second.TrySetResult();
            Assert.That(run.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
        }

        [Test]
        public void AbortedContext_CannotAffectSameStepOnNextRun()
        {
            var contexts = new List<GuideStepContext>();
            var work = new UniTaskCompletionSource();
            Initialize(new[] { Definition(FirstGuide, 1) }, (context, ct) =>
            {
                contexts.Add(context);
                context.SetWaitingFor("当前执行");
                return work.Task.AttachExternalCancellation(ct);
            });
            var first = guide.RunAsync();
            guide.StopAsync().GetAwaiter().GetResult();
            Assert.That(first.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Aborted));
            var second = guide.RunAsync();
            Assert.That(contexts[1], Is.Not.SameAs(contexts[0]));
            contexts[0].RequestSkip();
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(guide.WaitingFor, Is.EqualTo("当前执行"));
            work.TrySetResult();
            Assert.That(second.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
        }

        private void Initialize(IEnumerable<GuidePlan> definitions,
            Func<GuideStepContext, CancellationToken, UniTask> execute,
            IEnumerable<IGuideStartCondition> startConditions = null)
        {
            architecture = new ArchContext();
            store = new GuideStore();
            architecture.RegisterStore(store);
            architecture.RegisterSystem(new ConsumerGuideSystem(definitions, execute,
                startConditions ?? new[] { new TestStartCondition() }));
            architecture.InitializeAsync().GetAwaiter().GetResult();
            guide = architecture.GetSystem<IGuideSystem>();
        }

        private ControlledTeachingOperation NewControlledOperation()
        {
            var operation = new ControlledTeachingOperation();
            releaseCleanup.Add(() => operation.AllowCleanup.TrySetResult());
            return operation;
        }

        private static GuidePlan Definition(int guideId, params int[] stepIds)
        {
            var steps = new List<GuideStep>();
            foreach (var stepId in stepIds) steps.Add(new GuideStep(stepId, 9001));
            return new GuidePlan(guideId, steps, DefaultConditionType, 0, canSkip: true);
        }

        private GuideStore RestoreSavedOutcomes()
        {
            var json = JsonUtility.ToJson(store.GetData());
            var restored = new GuideStore();
            restored.ReplaceData(JsonUtility.FromJson<GuideStoreData>(json));
            return restored;
        }

        private void AssertNoSavedOutcome(int guideId)
        {
            Assert.That(store.IsFinished(guideId), Is.False);
            Assert.That(RestoreSavedOutcomes().IsFinished(guideId), Is.False);
            CollectionAssert.IsEmpty(store.GetData().CompletedGuideIds);
            CollectionAssert.IsEmpty(store.GetData().SkippedGuideIds);
        }

        private static Exception ReadFailure(UniTask<GuideExitReason?> task)
        {
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Faulted));
            return Assert.Catch(() => task.GetAwaiter().GetResult());
        }

        private static async UniTask ExecuteStepAsync(int stepId, UniTaskCompletionSource firstWork,
            List<string> trace, CancellationToken ct)
        {
            trace.Add($"work:{stepId}");
            try
            {
                if (stepId == 1) await firstWork.Task.AttachExternalCancellation(ct);
            }
            finally { trace.Add($"cleanup:{stepId}"); }
        }

        private static async UniTask FailTeachingAsync(Exception error, Action onCleanup)
        {
            try { await UniTask.FromException(error); }
            finally { onCleanup(); }
        }

        private sealed class ControlledTeachingOperation
        {
            private readonly UniTaskCompletionSource work = new();
            internal readonly UniTaskCompletionSource AllowCleanup = new();
            internal bool FinallyEntered;
            internal bool FinallyCompleted;

            internal async UniTask ExecuteAsync(CancellationToken ct)
            {
                try { await work.Task.AttachExternalCancellation(ct); }
                finally
                {
                    FinallyEntered = true;
                    await AllowCleanup.Task;
                    FinallyCompleted = true;
                }
            }
        }

        private sealed class TestStartCondition : IGuideStartCondition
        {
            internal Func<int, bool> Evaluate = _ => true;
            internal readonly List<int> EvaluatedParams = new();

            public int Type { get; }

            internal TestStartCondition(int type = DefaultConditionType) => Type = type;

            public bool CanStart(int paramId)
            {
                EvaluatedParams.Add(paramId);
                return Evaluate(paramId);
            }
        }

        private sealed class ConsumerGuideSystem : GuideSystemBase
        {
            private readonly IEnumerable<GuidePlan> definitions;
            private readonly Func<GuideStepContext, CancellationToken, UniTask> execute;
            private readonly IEnumerable<IGuideStartCondition> startConditions;

            internal ConsumerGuideSystem(IEnumerable<GuidePlan> definitions,
                Func<GuideStepContext, CancellationToken, UniTask> execute,
                IEnumerable<IGuideStartCondition> startConditions)
            {
                this.definitions = definitions;
                this.execute = execute;
                this.startConditions = startConditions;
            }

            protected override IEnumerable<GuidePlan> CreatePlans() => definitions;
            protected override IEnumerable<IGuideStartCondition> CreateStartConditions() => startConditions;
            protected override ProcedureBase CreateStepProcedure(GuideStepContext context)
                => new ConsumerTeachingProcedure(context, execute);
        }

        private sealed class ConsumerTeachingProcedure : GuideStepProcedure
        {
            private readonly Func<GuideStepContext, CancellationToken, UniTask> execute;

            internal ConsumerTeachingProcedure(GuideStepContext context,
                Func<GuideStepContext, CancellationToken, UniTask> execute) : base(context)
                => this.execute = execute;

            protected override UniTask OnExecuteAsync(CancellationToken ct) => execute(Context, ct);
        }
    }
}
