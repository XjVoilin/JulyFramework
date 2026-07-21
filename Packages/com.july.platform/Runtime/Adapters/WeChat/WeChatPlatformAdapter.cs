#if JULYGF_WX_MINIGAME
using System.Threading;
using Cysharp.Threading.Tasks;
using WeChatWASM;

namespace July.Platform
{
    /// <summary>WeChat Mini Game SDK adapter. The SDK is supplied by the host project.</summary>
    public sealed class WeChatPlatformAdapter : IPlatformAdapter
    {
        private readonly int _platformType;
        private IDeviceService _device;

        public int PlatformType => _platformType;

        public WeChatPlatformAdapter(int platformType)
        {
            _platformType = platformType;
        }

        public async UniTask ConfigureAsync(
            PlatformServiceRegistry registry,
            CancellationToken cancellationToken)
        {
            var completion = new UniTaskCompletionSource();
            using var cancellation = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));

            WX.InitSDK(_ => completion.TrySetResult());
            await completion.Task;
            cancellationToken.ThrowIfCancellationRequested();
            JsBridge.Init();

            registry.Register<IADsService>(new WeChatADsService());
            registry.Register<IAuthorizeService>(new WeChatAuthorizeService());
            registry.Register<IShareService>(new WeChatShareService());
            registry.Register<IDeviceService>(new WeChatDeviceService());
            registry.Register<ILifecycleService>(new WeChatLifecycleService());
            registry.Register<ISocialService>(new WeChatSocialService());
            registry.Register<ISubscribeService>(new WeChatSubscribeService());
            registry.Register<IBookmarkService>(new DefaultBookmarkService());
            registry.Register<IPurchaseService>(new DefaultPurchaseService());
            registry.Register<ILiveService>(new DefaultLiveService());
            registry.Register<ILoginService>(new WeChatLoginService());
            _device = registry.Get<IDeviceService>();
        }

        public void VibrateShort(VibrateType type) => _device?.VibrateShort(type);
        public void VibrateLong() => _device?.VibrateLong();
        public void Shutdown() => _device = null;
    }
}
#endif
