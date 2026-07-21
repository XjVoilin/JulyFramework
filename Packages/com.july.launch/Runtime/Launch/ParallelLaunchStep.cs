using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Launch
{
    public sealed class ParallelLaunchStep : ILaunchStep
    {
        private readonly ILaunchStep[] _steps;
        public string Name { get; }

        public ParallelLaunchStep(string name, params ILaunchStep[] steps)
        {
            Name = string.IsNullOrWhiteSpace(name) ? nameof(ParallelLaunchStep) : name;
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
            if (Array.Exists(_steps, step => step == null))
                throw new ArgumentException("并行步骤不能包含 null。", nameof(steps));
        }

        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var tasks = new UniTask<bool>[_steps.Length];
            for (var i = 0; i < _steps.Length; i++)
                tasks[i] = _steps[i].ExecuteAsync(ct);

            var results = await UniTask.WhenAll(tasks);
            for (var i = 0; i < results.Length; i++)
                if (!results[i]) return false;
            return true;
        }
    }
}
