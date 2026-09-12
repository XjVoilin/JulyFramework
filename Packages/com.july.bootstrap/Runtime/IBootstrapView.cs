using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Bootstrap
{
    public enum LaunchFailureAction { Retry, Restart }

    public readonly struct LaunchFailure
    {
        public string Stage { get; }
        public bool CanRetry { get; }
        public Exception Error { get; }

        public LaunchFailure(string stage, bool canRetry, Exception error = null)
        {
            Stage = stage;
            CanRetry = canRetry;
            Error = error;
        }
    }

    /// <summary>项目负责画面展示和用户选择，应用操作由 Bootstrap 执行。</summary>
    public interface IBootstrapView
    {
        void SetStepInfo(int completed, int total);
        UniTask<LaunchFailureAction> ShowFailureAsync(LaunchFailure failure, CancellationToken ct);
        void PrepareForGame();
        void Complete();
    }
}
