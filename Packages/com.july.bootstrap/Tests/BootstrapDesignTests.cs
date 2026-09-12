using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Launch;
using NUnit.Framework;

namespace July.Bootstrap.Tests
{
    public class BootstrapDesignTests
    {
        [Test]
        public void StandardConfigurationSealsTheCompletePlan()
        {
            var pipeline = new LaunchPipeline();
            var config = new BootstrapConfig();
            config.Analytics.Enabled = false;
            config.HotUpdate.RegistrarAssembly = "Game";
            config.HotUpdate.RegistrarType = "Game.Registrar";
            Bootstrap.Configure(pipeline, config, new RegistrarStepsTests.TestView(), Array.Empty<string>());
            Assert.That(pipeline.Count, Is.EqualTo(10));
            Assert.Throws<InvalidOperationException>(() => pipeline.Add(new Step(_ => UniTask.FromResult(true))));
        }

        [Test]
        public void CompletionWaitsForTheFinalStepAndPipelineCannotRunAgain()
        {
            var last = new UniTaskCompletionSource<bool>();
            var completed = false;
            var pipeline = new LaunchPipeline { OnCompleted = () => completed = true };
            pipeline.Add(new Step(_ => UniTask.FromResult(true))).Add(new Step(_ => last.Task));
            var task = pipeline.ExecuteAsync(default);
            Assert.That(completed, Is.False);
            last.TrySetResult(true);
            Assert.That(task.GetAwaiter().GetResult(), Is.True);
            Assert.That(completed, Is.True);
            Assert.Throws<InvalidOperationException>(() => pipeline.ExecuteAsync(default).GetAwaiter().GetResult());
        }

        [Test]
        public void FatalFailureIsReportedOnceAndDoesNotComplete()
        {
            var expected = new InvalidOperationException("Bad registration");
            var reports = 0;
            var completed = false;
            var pipeline = new LaunchPipeline
            {
                OnCompleted = () => completed = true,
                OnFailed = (_, error, _) =>
                {
                    Assert.That(error, Is.SameAs(expected));
                    reports++;
                    return UniTask.CompletedTask;
                }
            };
            pipeline.Add(new Step(_ => UniTask.FromException<bool>(expected)));
            Assert.That(Assert.Throws<InvalidOperationException>(() => pipeline.ExecuteAsync(default).GetAwaiter().GetResult()), Is.SameAs(expected));
            Assert.That(reports, Is.EqualTo(1));
            Assert.That(completed, Is.False);
        }

        [Test]
        public void CancellationDoesNotDisplayFatalFailureOrComplete()
        {
            var pipeline = new LaunchPipeline
            {
                OnCompleted = () => Assert.Fail("Cancelled startup completed"),
                OnFailed = (_, _, _) => throw new Exception("Cancellation reported as failure")
            };
            pipeline.Add(new Step(_ => UniTask.FromCanceled<bool>(new CancellationToken(true))));
            Assert.Throws<OperationCanceledException>(() => pipeline.ExecuteAsync(default).GetAwaiter().GetResult());
        }

        [Test]
        public void ObservationDistinguishesCancellationFromFault()
        {
            var outcome = LaunchStepOutcome.Succeeded;
            var step = new ObservedLaunchStep(new Step(_ => UniTask.FromCanceled<bool>(new CancellationToken(true))),
                result => outcome = result.Outcome);
            Assert.Throws<OperationCanceledException>(() => step.ExecuteAsync(default).GetAwaiter().GetResult());
            Assert.That(outcome, Is.EqualTo(LaunchStepOutcome.Cancelled));
        }

        [Test]
        public void LateSystemReceivesUpdatesOnlyAfterItsOwnInitialization()
        {
            var arch = new ArchContext();
            try
            {
                arch.InitializeAsync().GetAwaiter().GetResult();
                var system = new PendingSystem();
                arch.RegisterSystem(system);
                arch.Update(1);
                Assert.That(system.Updates, Is.Zero);
                var initializing = arch.InitializeAsync();
                arch.Update(1);
                Assert.That(system.Updates, Is.Zero);
                system.Ready.TrySetResult();
                initializing.GetAwaiter().GetResult();
                arch.Update(1);
                Assert.That(system.Updates, Is.EqualTo(1));
            }
            finally { arch.Shutdown(); }
        }

        private sealed class PendingSystem : SystemBase, IUpdatableSystem
        {
            internal readonly UniTaskCompletionSource Ready = new();
            internal int Updates;
            protected override UniTask OnInitializeAsync() => Ready.Task;
            public void OnUpdate(float deltaTime) => Updates++;
        }

        private sealed class Step : ILaunchStep
        {
            private readonly Func<CancellationToken, UniTask<bool>> _run;
            internal Step(Func<CancellationToken, UniTask<bool>> run) => _run = run;
            public string Name => "Test Step";
            public UniTask<bool> ExecuteAsync(CancellationToken ct) => _run(ct);
        }
    }
}
