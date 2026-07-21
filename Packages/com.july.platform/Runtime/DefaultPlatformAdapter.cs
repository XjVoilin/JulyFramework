using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Platform
{
    /// <summary>
    /// Editor and unsupported-platform adapter. It exposes deterministic local stubs
    /// for every common platform service so game code does not need null branches.
    /// </summary>
    public sealed class DefaultPlatformAdapter : IPlatformAdapter
    {
        private IDeviceService _device;

        public int PlatformType { get; }

        public DefaultPlatformAdapter(int platformType = 0) => PlatformType = platformType;

        public UniTask ConfigureAsync(PlatformServiceRegistry registry,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            registry.Register<IADsService>(new DefaultADsService());
            registry.Register<IAuthorizeService>(new DefaultAuthorizeService());
            registry.Register<IBookmarkService>(new DefaultBookmarkService());
            registry.Register<IDeviceService>(new DefaultDeviceService());
            registry.Register<ILifecycleService>(new DefaultLifecycleService());
            registry.Register<ILiveService>(new DefaultLiveService());
            registry.Register<IPurchaseService>(new DefaultPurchaseService());
            registry.Register<IShareService>(new DefaultShareService());
            registry.Register<ISocialService>(new DefaultSocialService());
            registry.Register<ISubscribeService>(new DefaultSubscribeService());
            registry.Register<ILoginService>(new DefaultLoginService());
            _device = registry.Get<IDeviceService>();
            return UniTask.CompletedTask;
        }

        public void VibrateShort(VibrateType type) => _device?.VibrateShort(type);
        public void VibrateLong() => _device?.VibrateLong();

        public void Shutdown() => _device = null;
    }
}
