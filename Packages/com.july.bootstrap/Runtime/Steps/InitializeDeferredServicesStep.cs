using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Launch;
using July.Platform;

namespace July.Bootstrap
{
    internal sealed class InitializeDeferredServicesStep : ILaunchStep
    {
        public string Name => "Initialize Deferred Platform Services";
        public UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ArchContext.Current.GetSystem<IPlatformSystem>().DeferAllServices();
            return UniTask.FromResult(true);
        }
    }
}
