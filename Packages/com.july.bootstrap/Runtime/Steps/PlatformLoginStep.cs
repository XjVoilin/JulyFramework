using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Launch;
using July.Logging;
using July.Platform;

namespace July.Bootstrap
{
    internal sealed class PlatformLoginStep : ILaunchStep
    {
        public string Name => "Platform Login";
        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var login = July.Arch.ArchContext.Current.GetSystem<IPlatformSystem>().GetService<ILoginService>()
                ?? throw new InvalidOperationException("Platform has no login service.");
            try
            {
                await login.LoginAsync(ct);
            }
            // 当前微信和抖音服务通过普通 Exception 上报 SDK 失败回调。
            catch (Exception error) when (error.GetType() == typeof(Exception))
            {
                ct.ThrowIfCancellationRequested();
                JLogger.LogError($"[PlatformLogin] {error}");
                return false;
            }
            ct.ThrowIfCancellationRequested();
            return true;
        }
    }

}
