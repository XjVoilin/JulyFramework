#if JULYGF_DY_MINIGAME
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TTSDK;

namespace July.Platform
{
    /// <summary>抖音小游戏 SDK 适配器；SDK 由接入项目提供。</summary>
    public sealed class TikTokPlatformAdapter : IPlatformAdapter
    {
        private readonly int _platformType;
        private readonly int _maxFramebufferLongEdge;
        private readonly string _rewardedAdUnitId;
        private IDeviceService _device;

        public int PlatformType => _platformType;

        public TikTokPlatformAdapter(int platformType, int maxFramebufferLongEdge, string rewardedAdUnitId)
        {
            if (maxFramebufferLongEdge <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxFramebufferLongEdge));

            _platformType = platformType;
            _maxFramebufferLongEdge = maxFramebufferLongEdge;
            _rewardedAdUnitId = rewardedAdUnitId;
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

            registry.Register<IADsService>(new TikTokADsService(_rewardedAdUnitId));
            registry.Register<IAuthorizeService>(new TikTokAuthorizeService());
            registry.Register<IShareService>(new TikTokShareService());
            registry.Register<IDeviceService>(
                new TikTokDeviceService(_maxFramebufferLongEdge));
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
