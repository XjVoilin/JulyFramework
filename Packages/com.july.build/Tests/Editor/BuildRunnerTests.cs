using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace July.Build.Tests
{
    public class BuildRunnerTests
    {
        [Test]
        public void Run_ExecutesStepsInOrder()
        {
            var calls = new List<string>();
            var host = new FakeHost();
            var result = new BuildRunner(host).Run(Context(), new IBuildStep[]
            {
                new FakeStep("A", execute: _ => { calls.Add("A"); return BuildStepResult.Success(); }),
                new FakeStep("B", execute: _ => { calls.Add("B"); return BuildStepResult.Success(); })
            });

            Assert.That(result.Succeeded, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(host.ClearCount, Is.EqualTo(1));
        }

        [Test]
        public void Run_ValidationFailurePreventsExecution()
        {
            var executed = false;
            var result = new BuildRunner(new FakeHost()).Run(Context(), new IBuildStep[]
            {
                new FakeStep("Invalid", _ => "缺少渠道配置"),
                new FakeStep("Later", execute: _ => { executed = true; return BuildStepResult.Success(); })
            });
            Assert.That(result.FailedStep, Is.EqualTo("Invalid"));
            Assert.That(executed, Is.False);
        }

        [Test]
        public void Run_InteractiveCancellationDoesNotSave()
        {
            var host = new FakeHost { ConfirmResult = false };
            var result = new BuildRunner(host).Run(Context(true),
                new[] { new FakeStep("Step") });
            Assert.That(result.Outcome, Is.EqualTo(BuildOutcome.Cancelled));
            Assert.That(host.SaveCount, Is.Zero);
        }

        private static BuildContext Context(bool interactive = false) =>
            new(BuildTarget.StandaloneWindows64, "windows", "test", "1.0.0", interactive);

        private sealed class FakeStep : IBuildStep
        {
            private readonly Func<BuildContext, string> _validate;
            private readonly Func<BuildContext, BuildStepResult> _execute;
            public string Name { get; }
            public FakeStep(string name, Func<BuildContext, string> validate = null,
                Func<BuildContext, BuildStepResult> execute = null)
            {
                Name = name;
                _validate = validate ?? (_ => null);
                _execute = execute ?? (_ => BuildStepResult.Success());
            }
            public string Validate(BuildContext context) => _validate(context);
            public BuildStepResult Execute(BuildContext context) => _execute(context);
        }

        private sealed class FakeHost : IBuildHost
        {
            public bool ConfirmResult = true;
            public int SaveCount;
            public int ClearCount;
            public bool Confirm(BuildContext context, int stepCount) => ConfirmResult;
            public void SaveAssets() => SaveCount++;
            public void ShowProgress(string stepName, int stepIndex, int stepCount) { }
            public void ClearProgress() => ClearCount++;
            public void Log(string message) { }
            public void LogError(string message) { }
        }
    }
}
