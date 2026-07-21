using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.Platform
{
    public sealed class DefaultPlatformAdapter : IPlatformAdapter
    {
        public int PlatformType { get; }

        public DefaultPlatformAdapter(int platformType = 0) => PlatformType = platformType;

        public UniTask ConfigureAsync(PlatformCapabilityRegistry registry,
            CancellationToken cancellationToken) => UniTask.CompletedTask;

        public void VibrateShort(VibrateType type) => Handheld.Vibrate();
        public void VibrateLong() => Handheld.Vibrate();
        public void Shutdown() { }
    }
}
