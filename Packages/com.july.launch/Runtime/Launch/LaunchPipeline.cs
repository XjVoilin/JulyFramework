using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Logging;

namespace July.Launch
{
    public class LaunchPipeline
    {
        private readonly List<ILaunchStep> _steps = new();

        public Action<string, int, int> OnStepBegin { get; set; }

        public LaunchPipeline Add(ILaunchStep step)
        {
            _steps.Add(step);
            return this;
        }

        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            for (var i = 0; i < _steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var step = _steps[i];
                OnStepBegin?.Invoke(step.Name, i + 1, _steps.Count);
                JLogger.Log($"[Launch] [{i + 1}/{_steps.Count}] {step.Name}");

                if (!await step.ExecuteAsync(ct))
                {
                    JLogger.Log($"[Launch] Aborted at: {step.Name}");
                    return false;
                }
            }

            return true;
        }
    }
}
