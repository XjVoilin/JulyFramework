using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Platform
{
    /// <summary>
    /// 编辑器及暂未支持平台的默认适配器，为所有公共平台服务提供
    /// 行为确定的本地替代实现，业务代码无需为缺少服务添加空值分支。
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
