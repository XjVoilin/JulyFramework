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
    /// Integration tests of the public Guide/Arch contracts. Teaching operations are ordinary
    /// consumer Procedures; cancellation, ordering, notifications and saving are production code.
    /// </summary>
    public sealed class GuideLifecycleTests
    {
        private const int FirstGuide = 7101;
        private const int SecondGuide = 7102;
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
                // A failed assertion must not strand an intentionally paused Procedure finally.
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
        public void OwnerCancellationCallbackThrows_AfterSynchronousCleanup_RunFaultsBeforeTerminalNotification()
        {
            var cancellationFailure = new InvalidOperationException("owner-cancellation-callback");
            var work = new UniTaskCompletionSource();
            var cleanedUp = false;
            GuideExitedEvent? terminal = null;
            Initialize(new[] { Definition(FirstGuide, 1) }, (_, ct) =>
                ExecuteWithThrowingCancellationAsync(work, cancellationFailure, () => cleanedUp = true, ct));
            architecture.Event.Subscribe<GuideExitedEvent>(e => terminal = e, this);
            using var owner = new CancellationTokenSource();
            var run = guide.RunAsync(owner.Token);

            // The outer owner token must not receive the inner callback failure or return a
            // falsely successful guide. The Procedure finally resumes inside CTS.Cancel.
            Assert.DoesNotThrow(() => owner.Cancel());

            Assert.That(cleanedUp, Is.True);
            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Faulted));
            var observed = ReadFailure(run);
            Assert.That(ContainsException(observed, cancellationFailure), Is.True);
            Assert.That(terminal.HasValue, Is.True);
            Assert.That(terminal.Value.Reason, Is.EqualTo(GuideExitReason.Faulted));
            Assert.That(ContainsException(terminal.Value.Error, cancellationFailure), Is.True,
                "Terminal notification must wait until Cancel has reported callback failures.");
            Assert.That(guide.LastFailure, Is.SameAs(observed));
            Assert.That(guide.IsRunning, Is.False);
            AssertNoSavedOutcome(FirstGuide);
        }

        [Test]
        public void ConcurrentStops_KeepRestartBlockedUntilBothStopWaitersHaveExited()
        {
            var operation = NewControlledOperation();
            Initialize(new[] { Definition(FirstGuide, 1) }, (_, ct) => operation.ExecuteAsync(ct));
            var run = guide.RunAsync();
            var firstStop = guide.StopAsync();
            var secondStop = guide.StopAsync();
            var restartRejected = false;
            var firstStopObserver = AfterAsync(firstStop, () =>
            {
                try { guide.RunAsync(); }
                catch (InvalidOperationException) { restartRejected = true; }
            });

            Assert.That(firstStopObserver.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(secondStop.Status, Is.EqualTo(UniTaskStatus.Pending));
            operation.AllowCleanup.TrySetResult();

            firstStopObserver.GetAwaiter().GetResult();
            secondStop.GetAwaiter().GetResult();
            Assert.That(restartRejected, Is.True,
                "The first Stop continuation must not reopen the system while the second Stop is pending.");
            Assert.That(run.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Aborted));
            Assert.That(guide.IsRunning, Is.False);
            AssertNoSavedOutcome(FirstGuide);
        }

        [Test]
        public void TerminalListenerRun_JoinsOldRun_AndNextGuideNeedsExplicitLaterTrigger()
        {
            var started = new List<int>();
            var joined = default(UniTask<GuideExitReason?>);
            var joinedInTerminal = false;
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
                joined = guide.RunAsync();
                joinedInTerminal = true;
            }, this);

            var first = guide.RunAsync();

            Assert.That(first.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
            Assert.That(joinedInTerminal, Is.True);
            Assert.That(runningDuringTerminal, Is.True);
            Assert.That(guideIdDuringTerminal, Is.EqualTo(FirstGuide));
            Assert.That(joined.GetAwaiter().GetResult(), Is.EqualTo(GuideExitReason.Completed));
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
        public void TerminalEventErrorHandlerThrows_DoesNotStrandCompletionOrUndoCommittedOutcome()
        {
            var listenerFailure = new InvalidOperationException("terminal-listener");
            var errorHandlerFailure = new InvalidOperationException("terminal-error-handler", listenerFailure);
            Initialize(new[] { Definition(FirstGuide, 1) }, (_, __) => UniTask.CompletedTask);
            architecture.Event.Subscribe<GuideExitedEvent>(_ => throw listenerFailure, this);
            EventBus.ErrorHandler = _ => throw errorHandlerFailure;

            var run = guide.RunAsync();

            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Faulted));
            Assert.That(ReadFailure(run), Is.SameAs(errorHandlerFailure));
            Assert.That(guide.LastFailure, Is.SameAs(errorHandlerFailure));
            Assert.That(guide.IsRunning, Is.False);
            Assert.That(RestoreSavedOutcomes().IsCompleted(FirstGuide), Is.True,
                "An observer failure cannot undo an already committed teaching outcome.");
            Assert.That(guide.StopAsync().Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }

        private void Initialize(IEnumerable<GuideDefinition> definitions,
            Func<GuideStepContext, CancellationToken, UniTask> execute)
        {
            architecture = new ArchContext();
            store = new GuideStore();
            architecture.RegisterStore(store);
            architecture.RegisterSystem(new ConsumerGuideSystem(definitions, execute));
            architecture.InitializeAsync().GetAwaiter().GetResult();
            guide = architecture.GetSystem<IGuideSystem>();
        }

        private ControlledTeachingOperation NewControlledOperation()
        {
            var operation = new ControlledTeachingOperation();
            releaseCleanup.Add(() => operation.AllowCleanup.TrySetResult());
            return operation;
        }

        private static GuideDefinition Definition(int guideId, params int[] stepIds)
        {
            var steps = new List<GuideStepDefinition>();
            foreach (var stepId in stepIds) steps.Add(new GuideStepDefinition(stepId, 9001));
            return new GuideDefinition(guideId, steps, canSkip: true);
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

        private static bool ContainsException(Exception root, Exception expected)
        {
            if (ReferenceEquals(root, expected)) return true;
            if (root is AggregateException aggregate)
                foreach (var child in aggregate.InnerExceptions)
                    if (ContainsException(child, expected)) return true;
            return root?.InnerException != null && ContainsException(root.InnerException, expected);
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

        private static async UniTask ExecuteWithThrowingCancellationAsync(UniTaskCompletionSource work,
            Exception error, Action onCleanup, CancellationToken ct)
        {
            var waiting = work.Task.AttachExternalCancellation(ct);
            // Registered last, so this throws before the waiting continuation resumes. CTS.Cancel
            // still only reports its AggregateException after all callbacks, including cleanup.
            using var registration = ct.Register(() => throw error);
            try { await waiting; }
            finally { onCleanup(); }
        }

        private static async UniTask FailTeachingAsync(Exception error, Action onCleanup)
        {
            try { await UniTask.FromException(error); }
            finally { onCleanup(); }
        }

        private static async UniTask AfterAsync(UniTask task, Action continuation)
        {
            await task;
            continuation();
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

        private sealed class ConsumerGuideSystem : GuideSystemBase
        {
            private readonly IEnumerable<GuideDefinition> definitions;
            private readonly Func<GuideStepContext, CancellationToken, UniTask> execute;

            internal ConsumerGuideSystem(IEnumerable<GuideDefinition> definitions,
                Func<GuideStepContext, CancellationToken, UniTask> execute)
            {
                this.definitions = definitions;
                this.execute = execute;
            }

            protected override IEnumerable<GuideDefinition> CreateGuides() => definitions;
            protected override bool CanStart(GuideDefinition definition) => true;
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
