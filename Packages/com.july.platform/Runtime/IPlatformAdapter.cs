using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Platform
{
    public interface IPlatformAdapter
    {
        int PlatformType { get; }
        UniTask ConfigureAsync(PlatformCapabilityRegistry registry, CancellationToken cancellationToken);
        void VibrateShort(VibrateType type);
        void VibrateLong();
        void Shutdown();
    }
}
