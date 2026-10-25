using July.Logging;
using July.Arch;
#if JULYGF_DY_MINIGAME
using System;
using Cysharp.Threading.Tasks;
using TTSDK;
using UnityEngine;
namespace July.Platform
{
    public class TikTokLiveService : ILiveService, INeedGetService, IArchNode
    {
        private const string Tag = "[TikTokLive]";
        private const int LiveSettingTimeoutMs = 3000;

        private TTLiveManager _liveManager;

        public Func<Type, object> ServiceGetter { get; set; }

        public bool IsInLive { get; private set; }
        public bool IsAnchor { get; private set; }
        public bool IsInstantPlay { get; private set; }
        public string GameProgress { get; private set; }

        public void Init()
        {
        }

        public void PostInit()
        {
            _liveManager = TT.GetLiveManager();
            JLogger.Log($"{Tag} PostInit liveManager={((_liveManager != null) ? "ok" : "null")}");
            if (_liveManager == null) return;

            RegisterInstantPlayStatusChange();
        }

        public async UniTask PostInitAsync()
        {
            if (_liveManager == null || !IsLiveLaunch()) return;

            JLogger.Log($"{Tag} GetLiveSetting requesting...");
            var tcs = new UniTaskCompletionSource();
            var param = new GetLiveSettingParam
            {
                Success = res =>
                {
                    IsInLive = res.IsVideoLive;
                    IsAnchor = res.IsAnchor;
                    IsInstantPlay = res.InstantPlay;
                    JLogger.Log($"{Tag} GetLiveSetting success: inLive={IsInLive}, anchor={IsAnchor}, instantPlay={IsInstantPlay}");
                    tcs.TrySetResult();
                },
                Fail = err =>
                {
                    JLogger.LogWarning($"{Tag} GetLiveSetting failed: {err}");
                    tcs.TrySetResult();
                },
            };
            _liveManager.GetLiveSetting(param);

            await tcs.Task.Timeout(TimeSpan.FromMilliseconds(LiveSettingTimeoutMs));

            if (IsInLive && !IsAnchor)
                await SyncGameProgressAsync();

            JLogger.Log($"{Tag} PostInitAsync completed");
        }

        public void ReportGameProgress(string progress)
        {
            if (_liveManager == null) return;

            JLogger.Log($"{Tag} ReportGameProgress: {progress}");
            var opt = new UploadGameProgressParam
            {
                Data = progress,
                Success = _ => JLogger.Log($"{Tag} UploadGameProgress success"),
                Fail = err => JLogger.LogWarning($"{Tag} UploadGameProgress failed: {err}"),
            };
            _liveManager.UploadGameProgress(opt);
        }


        private async UniTask SyncGameProgressAsync()
        {
            JLogger.Log($"{Tag} SyncGameProgress requesting...");
            var tcs = new UniTaskCompletionSource();
            var param = new SyncGameProgressParam
            {
                Success = res =>
                {
                    GameProgress = res.Data;
                    JLogger.Log($"{Tag} SyncGameProgress success: {GameProgress ?? "null"}");
                    tcs.TrySetResult();
                },
                Fail = err =>
                {
                    JLogger.LogWarning($"{Tag} SyncGameProgress failed: {err}");
                    tcs.TrySetResult();
                },
            };
            _liveManager.SyncGameProgress(param);

            await tcs.Task.Timeout(TimeSpan.FromMilliseconds(LiveSettingTimeoutMs));
        }

        private void RegisterInstantPlayStatusChange()
        {
            _liveManager.OnInstantPlayStatusChange(res =>
            {
                IsInstantPlay = res.InstantPlayStatus;
                JLogger.Log($"{Tag} InstantPlayStatusChange: {IsInstantPlay}");

                if (IsInstantPlay && !IsInLive && IsLiveLaunch())
                {
                    RefreshLiveSettingAsync().Forget();
                }
            });
            JLogger.Log($"{Tag} RegisterInstantPlayStatusChange done");
        }

        private async UniTaskVoid RefreshLiveSettingAsync()
        {
            JLogger.Log($"{Tag} RefreshLiveSetting triggered by InstantPlayStatusChange");
            var tcs = new UniTaskCompletionSource();
            var param = new GetLiveSettingParam
            {
                Success = res =>
                {
                    IsInLive = res.IsVideoLive;
                    IsAnchor = res.IsAnchor;
                    JLogger.Log($"{Tag} RefreshLiveSetting success: inLive={IsInLive}, anchor={IsAnchor}");
                    tcs.TrySetResult();
                },
                Fail = err =>
                {
                    JLogger.LogWarning($"{Tag} RefreshLiveSetting failed: {err}");
                    tcs.TrySetResult();
                },
            };
            _liveManager.GetLiveSetting(param);
            await tcs.Task.Timeout(TimeSpan.FromMilliseconds(LiveSettingTimeoutMs));
        }

        private bool IsLiveLaunch() =>
            this.GetService<ILifecycleService>().LatestContext.Source == LaunchSource.Live;
    }
}
#endif
