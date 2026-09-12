#if JULYGF_WX_MINIGAME
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using WeChatWASM;

namespace July.Platform
{
    /// <summary>微信小游戏 SDK 适配器；SDK 由接入项目提供。</summary>
    public sealed class WeChatPlatformAdapter : IPlatformAdapter
    {
        private readonly int _platformType;
        private readonly int _maxFramebufferLongEdge;
        private readonly string _rewardedAdUnitId;
        private IDeviceService _device;

        public int PlatformType => _platformType;

        public WeChatPlatformAdapter(int platformType, int maxFramebufferLongEdge, string rewardedAdUnitId)
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

            WX.InitSDK(_ => completion.TrySetResult());
            await completion.Task;
            cancellationToken.ThrowIfCancellationRequested();
            JsBridge.Init();

            registry.Register<IADsService>(new WeChatADsService(_rewardedAdUnitId));
            registry.Register<IAuthorizeService>(new WeChatAuthorizeService());
            registry.Register<IShareService>(new WeChatShareService());
            registry.Register<IDeviceService>(
                new WeChatDeviceService(_maxFramebufferLongEdge));
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
