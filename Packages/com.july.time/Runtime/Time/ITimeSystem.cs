using System;

namespace July.Time
{
    /// <summary>
    /// 时间系统接口——提供游戏时间查询、定时器调度和服务器时钟校准。
    /// 通过 Scope.GetSystem&lt;ITimeSystem&gt;() 获取。
    /// </summary>
    public interface ITimeSystem
    {
        #region Time Properties

        float GameTime { get; }
        float RealTime { get; }
        float DeltaTime { get; }
        /// <summary>Unity 物理更新的固定时间步长（秒）。</summary>
        float FixedDeltaTime { get; }
        float UnscaledDeltaTime { get; }
        int FrameCount { get; }
        float TimeScale { get; set; }

        #endregion

        #region Server Time

        DateTime ServerTimeUtc { get; }
        long ServerTimeSeconds { get; }
        bool IsServerTimeSynced { get; }

        /// <summary>
        /// 同步服务器 UTC 时间基准。
        /// Local 类型会转换为 UTC；由于该接口约定传入 UTC，Unspecified 类型会按 UTC 解释。
        /// </summary>
        void SyncServerTime(DateTime serverTimeUtc);

        #endregion

        #region Timer

        int ScheduleOnce(float delay, Action callback, bool useRealTime = false);
        int ScheduleRepeat(float interval, Action callback, bool useRealTime = false, int repeatCount = -1);
        /// <summary>
        /// 查询有效定时器距离下次触发的剩余秒数，最小为零；暂停时保持不变。
        /// 不存在、已取消或已完成的定时器返回 false。到期回调期间仍可查询，返回零。
        /// </summary>
        bool TryGetTimerRemainingTime(int timerId, out float seconds);
        bool CancelTimer(int timerId);
        void CancelAllTimers();
        bool PauseTimer(int timerId);
        bool ResumeTimer(int timerId);

        #endregion

    }
}
