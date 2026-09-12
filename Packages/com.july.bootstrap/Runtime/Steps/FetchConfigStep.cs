using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Launch;
using July.Config;
using July.Logging;
using July.Release;
using YooAsset;

namespace July.Bootstrap
{
    internal sealed class FetchConfigStep : ILaunchStep
    {
        private readonly ReleaseSettings _config;
        private readonly EPlayMode _playMode;
        private readonly LogChannel _logChannels;
        public string Name => "Fetch Config";
        internal FetchConfigStep(ReleaseSettings config, EPlayMode playMode, LogChannel logChannels)
        { _config = config; _playMode = playMode; _logChannels = logChannels; }

        public async UniTask<bool> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var config = _config;
#if JULYGF_DY_MINIGAME && !JULYGF_WX_MINIGAME
            const string platform = "TikTok";
#else
            const string platform = "WeChat";
#endif
            var snapshot = new ReleaseConfigSnapshot(config.Env, config.CdnUrl, config.ConfigServer.Get, platform);
#if UNITY_EDITOR
            var remote = _playMode == EPlayMode.HostPlayMode || _playMode == EPlayMode.WebPlayMode;
#else
            var remote = true;
#endif
            if (remote && !snapshot.TryApplyCached(WebGLConfigCache.GetCachedJson()))
            {
                try { await snapshot.FetchAsync(cancellationToken: ct); }
                catch (InvalidOperationException error)
                {
                    ct.ThrowIfCancellationRequested();
                    JLogger.LogError($"[FetchConfig] {error}");
                    return false;
                }
            }
            else if (!remote) snapshot.FillDefaults();
            ct.ThrowIfCancellationRequested();
            var result = new LaunchInfo(snapshot.Env, snapshot.ServerUrl, snapshot.PlanVersion,
                snapshot.IsAudit, remote ? snapshot.GetRemoteMainURL() : string.Empty);
            July.Arch.ArchContext.Current.GetStore<LaunchInfoStore>().SetResult(result);
            JLogger.InitLogChannels(_logChannels);
            return true;
        }
    }
}
