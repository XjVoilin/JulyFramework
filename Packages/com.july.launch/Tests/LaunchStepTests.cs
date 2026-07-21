using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace July.Launch.Tests
{
    public class LaunchStepTests
    {
        [Test]
        public async Task Retry_RunsUntilSuccess()
        {
            var executions = 0;
            var prompts = 0;
            var step = new RetryLaunchStep(
                new DelegateStep("retry", _ => UniTask.FromResult(++executions == 3)),
                (_, _) => { prompts++; return UniTask.FromResult(true); });

            Assert.That(await step.ExecuteAsync(default), Is.True);
            Assert.That(executions, Is.EqualTo(3));
            Assert.That(prompts, Is.EqualTo(2));
        }

        [Test]
        public async Task Parallel_ReturnsFalseWhenAnyStepFails()
        {
            var step = new ParallelLaunchStep("parallel",
                new DelegateStep("ok", _ => UniTask.FromResult(true)),
                new DelegateStep("fail", _ => UniTask.FromResult(false)));
            Assert.That(await step.ExecuteAsync(default), Is.False);
        }

        [Test]
        public async Task Observed_ReportsFailure()
        {
            LaunchStepObservation observation = default;
            var step = new ObservedLaunchStep(
                new DelegateStep("observe", _ => UniTask.FromResult(false)),
                value => observation = value);
            await step.ExecuteAsync(default);
            Assert.That(observation.Outcome, Is.EqualTo(LaunchStepOutcome.Failed));
            Assert.That(observation.Name, Is.EqualTo("observe"));
        }

        private sealed class DelegateStep : ILaunchStep
        {
            private readonly Func<CancellationToken, UniTask<bool>> _execute;
            public string Name { get; }
            public DelegateStep(string name, Func<CancellationToken, UniTask<bool>> execute)
            {
                Name = name;
                _execute = execute;
            }
            public UniTask<bool> ExecuteAsync(CancellationToken ct) => _execute(ct);
        }
    }
}
