using System.Threading;
using Cysharp.Threading.Tasks;
using July.Analytics;
using July.Arch;
using July.Launch;
using July.Persistence;
using July.Platform;
using July.Release;
using July.UI;
using UnityEngine;

namespace July.Bootstrap
{
    internal sealed class BootArchStep : ILaunchStep
    {
        private readonly PlatformConfig _platform;
        private readonly ThinkingDataConfig _analytics;
        private readonly ReleaseSettings _release;
        public string Name => "Initialize Foundation";

        internal BootArchStep(PlatformConfig platform, ThinkingDataConfig analytics, ReleaseSettings release)
        { _platform = platform; _analytics = analytics; _release = release; }

        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
#if JULYGF_DY_MINIGAME
            PlatformPreferences.SetAdapter(new TikTokPreferencesAdapter());
#else
            PlatformPreferences.UseUnityPlayerPrefs();
#endif
            var arch = ArchContext.Current;
            arch.RegisterStore(new LaunchInfoStore());
            arch.RegisterSystem(CreateAnalytics());
            var platform = new PlatformSystem(CreatePlatformAdapter());
            arch.RegisterSystem(platform);
            arch.RegisterSystem(new PlatformSafeAreaBridgeSystem(platform));
            await arch.InitializeAsync(ct);
            ct.ThrowIfCancellationRequested();
            return true;
        }

        private AnalyticsSystem CreateAnalytics()
        {
            if (!_analytics.Enabled) return new AnalyticsSystem();
            var options = new ThinkingDataOptions(_analytics.AppId, _analytics.ServerUrl)
            {
                IsProduction = ReleaseConfigSnapshot.ResolveEnvironment(_release.Env) == ReleaseEnvironment.Prod,
                ForwardUnityErrors = true,
#if JULYGF_DEBUG
                EnableLog = _analytics.EnableLog,
#else
                EnableLog = false,
#endif
            };
            return new AnalyticsSystem(new ThinkingDataChannel(options));
        }

        private IPlatformAdapter CreatePlatformAdapter()
        {
#if JULYGF_WX_MINIGAME
            return new WeChatPlatformAdapter(3, _platform.MaxFramebufferLongEdge, _platform.WeChatRewardedAdUnitId);
#elif JULYGF_DY_MINIGAME
            return new TikTokPlatformAdapter(4, _platform.MaxFramebufferLongEdge, _platform.TikTokRewardedAdUnitId);
#else
            return new DefaultPlatformAdapter(1);
#endif
        }
    }

    internal sealed class PlatformSafeAreaBridgeSystem : SystemBase
    {
        private readonly IPlatformSystem _platform;
        internal PlatformSafeAreaBridgeSystem(IPlatformSystem platform) => _platform = platform;
        protected override UniTask OnInitializeAsync()
        {
            SafeAreaAdapter.SafeAreaOverride = () =>
                _platform.GetService<IDeviceService>()?.GetSafeArea() ?? Screen.safeArea;
            return UniTask.CompletedTask;
        }
        protected override void OnShutdown() => SafeAreaAdapter.SafeAreaOverride = null;
    }
}
