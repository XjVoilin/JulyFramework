using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Platform
{
    public interface IPlatformCapability
    {
        UniTask InitializeAsync(CancellationToken cancellationToken);
        void Shutdown();
    }

    public interface IDeferredPlatformCapability
    {
        void Defer();
    }
}
