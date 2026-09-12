using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Logging;

namespace July.Launch
{
    /// <summary>按明确的步骤顺序执行一次启动计划。</summary>
    public class LaunchPipeline
    {
        private readonly List<ILaunchStep> _steps = new();
        private bool _sealed;
        private bool _executed;
        public int Count => _steps.Count;
        public Action<string, int, int> OnStepBegin { get; set; }
        public Action OnCompleted { get; set; }
        public Func<string, Exception, CancellationToken, UniTask> OnFailed { get; set; }

        public LaunchPipeline Add(ILaunchStep step)
        {
            if (_sealed) throw new InvalidOperationException("The launch plan is sealed.");
            _steps.Add(step ?? throw new ArgumentNullException(nameof(step)));
            return this;
        }

        public void Seal() => _sealed = true;

        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            if (_executed) throw new InvalidOperationException("A launch pipeline can execute only once.");
            _executed = true;
            Seal();
            for (var i = 0; i < _steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var step = _steps[i];
                OnStepBegin?.Invoke(step.Name, i + 1, _steps.Count);
                JLogger.Log($"[Launch] [{i + 1}/{_steps.Count}] {step.Name}");
                bool succeeded;
                try { succeeded = await step.ExecuteAsync(ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    if (OnFailed != null) await OnFailed(step.Name, error, ct);
                    throw;
                }
                ct.ThrowIfCancellationRequested();
                if (!succeeded)
                {
                    if (OnFailed != null) await OnFailed(step.Name, null, ct);
                    return false;
                }
            }
            ct.ThrowIfCancellationRequested();
            OnCompleted?.Invoke();
            return true;
        }
    }
}
