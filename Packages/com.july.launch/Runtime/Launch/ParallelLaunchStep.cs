using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Launch
{
    /// <summary>Executes independent launch steps concurrently and succeeds when all succeed.</summary>
    public sealed class ParallelLaunchStep : ILaunchStep
    {
        private readonly ILaunchStep[] _steps;
        public string Name { get; }

        public ParallelLaunchStep(string name, params ILaunchStep[] steps)
        {
            Name = string.IsNullOrWhiteSpace(name) ? nameof(ParallelLaunchStep) : name;
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
            if (Array.Exists(_steps, step => step == null))
                throw new ArgumentException("Parallel steps cannot contain null.", nameof(steps));
        }

        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var tasks = new UniTask<bool>[_steps.Length];
            for (var index = 0; index < _steps.Length; index++)
                tasks[index] = _steps[index].ExecuteAsync(ct);

            var results = await UniTask.WhenAll(tasks);
            for (var index = 0; index < results.Length; index++)
            {
                if (!results[index]) return false;
            }
            return true;
        }
    }
}
