using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Launch
{
    /// <summary>失败后由调用方决定是否重试；异常和取消保持原语义。</summary>
    public sealed class RetryLaunchStep : ILaunchStep
    {
        private readonly ILaunchStep _inner;
        private readonly Func<int, CancellationToken, UniTask<bool>> _shouldRetry;
        public string Name => _inner.Name;

        public RetryLaunchStep(ILaunchStep inner,
            Func<int, CancellationToken, UniTask<bool>> shouldRetry)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _shouldRetry = shouldRetry ?? throw new ArgumentNullException(nameof(shouldRetry));
        }

        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            var failedAttempts = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (await _inner.ExecuteAsync(ct)) return true;
                failedAttempts++;
                if (!await _shouldRetry(failedAttempts, ct)) return false;
            }
        }
    }
}
