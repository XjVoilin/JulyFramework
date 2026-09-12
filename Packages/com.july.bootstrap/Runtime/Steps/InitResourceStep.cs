using System.Threading;
using Cysharp.Threading.Tasks;
using July.Launch;
using July.Resource.YooAsset;
using YooAsset;

namespace July.Bootstrap
{
    internal sealed class InitResourceStep : ILaunchStep
    {
        private readonly ResourceStartupConfig _options;
        public string Name => "Init Resource";
        internal InitResourceStep(ResourceStartupConfig options) => _options = options;

        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var remoteUrl = July.Arch.ArchContext.Current.GetStore<LaunchInfoStore>().Current.RemoteUrl;
            var options = new YooAssetOptions
            {
                PackageName = _options.PackageName,
                PlayMode = ResolvePlayMode(_options.PlayMode),
                DefaultHostServer = remoteUrl,
                FallbackHostServer = remoteUrl,
                SetAsDefaultPackage = true,
                UpdateManifestAfterInitialization = true,
            };
#if UNITY_WEBGL && JULYGF_WX_MINIGAME
            if (options.PlayMode == EPlayMode.WebPlayMode)
                options.CreateInitializeParameters = _ => WeChatYooAssetFileSystem.CreateInitializeParameters(remoteUrl);
#elif UNITY_WEBGL && JULYGF_DY_MINIGAME
            if (options.PlayMode == EPlayMode.WebPlayMode)
                options.CreateInitializeParameters = _ => TikTokYooAssetFileSystem.CreateInitializeParameters(remoteUrl);
#endif
            var arch = July.Arch.ArchContext.Current;
            arch.RegisterSystem(new YooAssetResourceSystem(options));
            // 资源系统从初始化开始就由 Arch 持有，初始化完成后继续沿用同一生命周期。
            // 资源封装会等待共享的底层初始化操作结束，再执行关闭清理。
            await arch.InitializeAsync(ct);
            ct.ThrowIfCancellationRequested();
            return true;
        }

        private static EPlayMode ResolvePlayMode(EPlayMode configured)
        {
#if UNITY_EDITOR
            return configured;
#elif JULYGF_WX_MINIGAME || JULYGF_DY_MINIGAME
            return EPlayMode.WebPlayMode;
#else
            return configured == EPlayMode.EditorSimulateMode ? EPlayMode.OfflinePlayMode : configured;
#endif
        }
    }
}