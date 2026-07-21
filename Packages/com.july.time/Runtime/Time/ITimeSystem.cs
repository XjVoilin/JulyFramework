using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Time
{
    /// <summary>
    /// 时间系统接口 — 时间查询、定时器调度、服务器时间同步、格式化。
    /// 通过 Scope.GetSystem&lt;ITimeSystem&gt;() 获取。
    /// </summary>
    public interface ITimeSystem
    {
        #region Time Properties

        float GameTime { get; }
        float RealTime { get; }
        float DeltaTime { get; }
        float UnscaledDeltaTime { get; }
        int FrameCount { get; }
        float TimeScale { get; set; }

        #endregion

        #region Server Time

        DateTime ServerTimeUtc { get; }
        DateTime ServerTimeLocal { get; }
        long ServerTimeSeconds { get; }
        bool IsServerTimeSynced { get; }
        double ServerTimeOffset { get; }

        void SyncServerTime(DateTime serverTimeUtc);
        UniTask<bool> SyncServerTimeFromNetworkAsync(string ntpServer = null, CancellationToken cancellationToken = default);

        #endregion

        #region Timer

        int ScheduleOnce(float delay, Action callback, bool useRealTime = false);
        int ScheduleRepeat(float interval, Action callback, bool useRealTime = false, int repeatCount = -1);
        bool CancelTimer(int timerId);
        void CancelAllTimers();
        bool PauseTimer(int timerId);
        bool ResumeTimer(int timerId);

        #endregion

        #region Formatting

        string FormatTime(float seconds, string format = null);
        string FormatTimeSpan(TimeSpan timeSpan, string format = null);

        #endregion
    }
}
