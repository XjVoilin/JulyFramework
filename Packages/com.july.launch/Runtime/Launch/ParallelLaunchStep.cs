using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Launch
{
    /// <summary>并行执行互相独立的操作；失败时取消其余操作，等待全部收尾后再返回。</summary>
    public sealed class ParallelLaunchStep : ILaunchStep
    {
        private readonly ILaunchStep[] _steps;
        public string Name { get; }
        public ParallelLaunchStep(string name, params ILaunchStep[] steps)
        {
            Name = name;
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
            if (Array.Exists(steps, step => step == null))
                throw new ArgumentException("Parallel steps cannot contain null.", nameof(steps));
        }
        public UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            var operations = new Func<CancellationToken, UniTask<bool>>[_steps.Length];
            for (var i = 0; i < _steps.Length; i++) operations[i] = _steps[i].ExecuteAsync;
            return RunAsync(ct, operations);
        }

        public static async UniTask<bool> RunAsync(CancellationToken ct,
            params Func<CancellationToken, UniTask<bool>>[] operations)
        {
            ct.ThrowIfCancellationRequested();
            using var group = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var errors = new Exception[operations.Length];
            var tasks = new UniTask<bool>[operations.Length];
            var failed = false;
            for (var i = 0; i < operations.Length; i++)
                tasks[i] = RunOne(i);

            await UniTask.WhenAll(tasks);
            ct.ThrowIfCancellationRequested();
            foreach (var error in errors)
                if (error != null && !(error is OperationCanceledException))
                    ExceptionDispatchInfo.Capture(error).Throw();
            if (failed) return false;
            foreach (var error in errors)
                if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            return true;

            async UniTask<bool> RunOne(int index)
            {
                try
                {
                    group.Token.ThrowIfCancellationRequested();
                    var result = await operations[index](group.Token);
                    if (!result)
                    {
                        failed = true;
                        group.Cancel();
                    }
                    return result;
                }
                catch (Exception error)
                {
                    errors[index] = error;
                    group.Cancel();
                    return false;
                }
            }
        }
    }
}
