using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Launch
{
    public enum LaunchStepOutcome { Succeeded, Failed, Faulted }

    public readonly struct LaunchStepObservation
    {
        public string Name { get; }
        public LaunchStepOutcome Outcome { get; }
        public TimeSpan Elapsed { get; }
        public Exception Exception { get; }

        public LaunchStepObservation(string name, LaunchStepOutcome outcome,
            TimeSpan elapsed, Exception exception = null)
        {
            Name = name;
            Outcome = outcome;
            Elapsed = elapsed;
            Exception = exception;
        }
    }

    public sealed class ObservedLaunchStep : ILaunchStep
    {
        private readonly ILaunchStep _inner;
        private readonly Action<LaunchStepObservation> _observe;
        public string Name => _inner.Name;

        public ObservedLaunchStep(ILaunchStep inner, Action<LaunchStepObservation> observe)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _observe = observe ?? throw new ArgumentNullException(nameof(observe));
        }

        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            var startedAt = DateTime.UtcNow;
            try
            {
                var succeeded = await _inner.ExecuteAsync(ct);
                _observe(new LaunchStepObservation(Name,
                    succeeded ? LaunchStepOutcome.Succeeded : LaunchStepOutcome.Failed,
                    DateTime.UtcNow - startedAt));
                return succeeded;
            }
            catch (Exception exception)
            {
                _observe(new LaunchStepObservation(Name, LaunchStepOutcome.Faulted,
                    DateTime.UtcNow - startedAt, exception));
                throw;
            }
        }
    }
}
