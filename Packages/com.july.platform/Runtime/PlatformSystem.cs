using System;
using Cysharp.Threading.Tasks;
using July.Arch;

namespace July.Platform
{
    public sealed class PlatformSystem : SystemBase, IPlatformSystem
    {
        private readonly IPlatformAdapter _adapter;
        private readonly PlatformCapabilityRegistry _registry = new();

        public int PlatformType => _adapter.PlatformType;

        public PlatformSystem(IPlatformAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        protected override async UniTask OnInitializeAsync()
        {
            await _adapter.ConfigureAsync(_registry, default);
            await _registry.InitializeAsync(default);
        }

        public T GetService<T>() where T : class => _registry.Get<T>();
        public void DeferAllServices() => _registry.DeferAll();
        public void VibrateShort(VibrateType type = VibrateType.Light) => _adapter.VibrateShort(type);
        public void VibrateLong() => _adapter.VibrateLong();

        protected override void OnShutdown()
        {
            _registry.Shutdown();
            _adapter.Shutdown();
        }
    }
}
