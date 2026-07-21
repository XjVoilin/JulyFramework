#if JULYGF_DY_MINIGAME
using System.Threading;
using Cysharp.Threading.Tasks;
using TTSDK;

namespace July.Platform
{
    /// <summary>TikTok Mini Game SDK adapter. The SDK is supplied by the host project.</summary>
    public sealed class TikTokPlatformAdapter : IPlatformAdapter
    {
        private readonly int _platformType;
        private IDeviceService _device;

        public int PlatformType => _platformType;

        public TikTokPlatformAdapter(int platformType)
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

            TT.InitSDK((_, _) => completion.TrySetResult());
            await completion.Task;
            cancellationToken.ThrowIfCancellationRequested();

            registry.Register<IADsService>(new TikTokADsService());
            registry.Register<IAuthorizeService>(new TikTokAuthorizeService());
            registry.Register<IShareService>(new TikTokShareService());
            registry.Register<IDeviceService>(new TikTokDeviceService());
            registry.Register<ILifecycleService>(new TikTokLifecycleService());
            registry.Register<IPurchaseService>(new TikTokPurchaseService());
            registry.Register<ISocialService>(new TikTokSocialService());
            registry.Register<IBookmarkService>(new TikTokBookmarkService());
            registry.Register<ILiveService>(new TikTokLiveService());
            var subscribe = new TikTokSubscribeService();
            registry.Register<ISubscribeService>(subscribe);
            registry.Register<ITikTokSubscribeService>(subscribe);
            registry.Register<ILoginService>(new TikTokLoginService());
            registry.Register<ITikTokFeedService>(new TikTokFeedService());
            _device = registry.Get<IDeviceService>();
        }

        public void VibrateShort(VibrateType type) => _device?.VibrateShort(type);
        public void VibrateLong() => _device?.VibrateLong();
        public void Shutdown() => _device = null;
    }
}
#endif
