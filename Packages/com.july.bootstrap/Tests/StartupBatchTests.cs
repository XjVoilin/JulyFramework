using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace July.Bootstrap.Tests
{
    public class StartupBatchTests
    {
        [Test]
        public void FailureCancelsButDrainsAnUncancellableSibling()
        {
            var first = new UniTaskCompletionSource<bool>();
            var second = new UniTaskCompletionSource<bool>();
            CancellationToken siblingToken = default;
            var result = July.Launch.ParallelLaunchStep.RunAsync(default, _ => first.Task,
                token => { siblingToken = token; return second.Task; });
            first.TrySetResult(false);
            Assert.That(siblingToken.IsCancellationRequested, Is.True);
            Assert.That(result.Status, Is.EqualTo(UniTaskStatus.Pending));
            second.TrySetResult(true);
            Assert.That(result.GetAwaiter().GetResult(), Is.False);
        }

        [Test]
        public void FaultIsRethrownOnlyAfterSiblingsFinish()
        {
            var first = new UniTaskCompletionSource<bool>();
            var second = new UniTaskCompletionSource<bool>();
            var error = new InvalidOperationException("Broken contract");
            var result = July.Launch.ParallelLaunchStep.RunAsync(default, _ => first.Task, _ => second.Task);
            first.TrySetException(error);
            Assert.That(result.Status, Is.EqualTo(UniTaskStatus.Pending));
            second.TrySetCanceled();
            Assert.That(Assert.Throws<InvalidOperationException>(() => result.GetAwaiter().GetResult()), Is.SameAs(error));
        }

        [Test]
        public void ParentCancellationDrainsBeforeThrowing()
        {
            using var parent = new CancellationTokenSource();
            var pending = new UniTaskCompletionSource<bool>();
            var result = July.Launch.ParallelLaunchStep.RunAsync(parent.Token, _ => pending.Task);
            parent.Cancel();
            Assert.That(result.Status, Is.EqualTo(UniTaskStatus.Pending));
            pending.TrySetResult(true);
            Assert.Throws<OperationCanceledException>(() => result.GetAwaiter().GetResult());
        }

        [Test]
        public void RetryDoesNotOverlapThePreviousAttempt()
        {
            var first = new UniTaskCompletionSource<bool>();
            var sibling = new UniTaskCompletionSource<bool>();
            var prompts = 0;
            var attempts = 0;
            var retry = new July.Launch.RetryLaunchStep(new BatchStep(token =>
            {
                attempts++;
                return attempts == 1
                    ? July.Launch.ParallelLaunchStep.RunAsync(token, _ => first.Task, _ => sibling.Task)
                    : July.Launch.ParallelLaunchStep.RunAsync(token, _ => UniTask.FromResult(true));
            }), (_, token) => { prompts++; return UniTask.FromResult(true); });
            var result = retry.ExecuteAsync(default);
            first.TrySetResult(false);
            Assert.That(prompts, Is.Zero);
            Assert.That(attempts, Is.EqualTo(1));
            sibling.TrySetResult(true);
            Assert.That(result.GetAwaiter().GetResult(), Is.True);
            Assert.That(prompts, Is.EqualTo(1));
            Assert.That(attempts, Is.EqualTo(2));
        }

        [Test]
        public void SiblingCancellationDoesNotReplaceARecoverableFailure()
        {
            var first = new UniTaskCompletionSource<bool>();
            var second = new UniTaskCompletionSource<bool>();
            var result = July.Launch.ParallelLaunchStep.RunAsync(default, _ => first.Task, _ => second.Task);
            first.TrySetResult(false);
            second.TrySetCanceled();
            Assert.That(result.GetAwaiter().GetResult(), Is.False);
        }

        [Test]
        public void AllSuccessfulBranchesProduceSuccess()
        {
            Assert.That(July.Launch.ParallelLaunchStep.RunAsync(default,
                _ => UniTask.FromResult(true), _ => UniTask.FromResult(true)).GetAwaiter().GetResult(), Is.True);
        }

        [Test]
        public void AlreadyCancelledParentStartsNothing()
        {
            using var parent = new CancellationTokenSource();
            parent.Cancel();
            var starts = 0;
            var result = July.Launch.ParallelLaunchStep.RunAsync(parent.Token,
                _ => { starts++; return UniTask.FromResult(true); });
            Assert.Throws<OperationCanceledException>(() => result.GetAwaiter().GetResult());
            Assert.That(starts, Is.Zero);
        }

        [Test]
        public void AssemblyNamesAreDeduplicatedWithoutChangingDependencyOrder()
        {
            var names = BootstrapAssemblyLoader.Normalize(new[] { "Game.Runtime", "MiniGames.dll", "Game.Runtime.dll" });
            Assert.That(names, Is.EqualTo(new[] { "Game.Runtime.dll", "MiniGames.dll" }));
        }

        [Test]
        public void InvalidAssemblyNamesFailAtTheConfigurationBoundary()
        {
            Assert.Throws<ArgumentException>(() => BootstrapAssemblyLoader.Normalize(new[] { "../Game.Runtime" }));
            Assert.Throws<ArgumentException>(() => BootstrapAssemblyLoader.Normalize(new[] { "" }));
        }

        private sealed class BatchStep : July.Launch.ILaunchStep
        {
            private readonly Func<CancellationToken, UniTask<bool>> _run;
            public string Name => "Batch";
            public BatchStep(Func<CancellationToken, UniTask<bool>> run) => _run = run;
            public UniTask<bool> ExecuteAsync(CancellationToken ct) => _run(ct);
        }
    }
}
