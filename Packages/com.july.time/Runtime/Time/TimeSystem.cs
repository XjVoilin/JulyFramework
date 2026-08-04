using System;
using System.Collections.Generic;
using System.Diagnostics;
using July.Arch;
using July.Logging;
using UnityEngine;
using UnityTime = UnityEngine.Time;

namespace July.Time
{
    public class TimeSystem : SystemBase, ITimeSystem, IUpdatableSystem
    {
        private int _nextTimerId = 1;
        private readonly HashSet<int> _activeTimerIds = new();
        private readonly Dictionary<int, TimerInfo> _timers = new();
        private readonly List<TimerInfo> _snapshot = new(16);
        private readonly List<int> _timersToRemove = new(8);
        private readonly object _timerLock = new();
        private readonly ITimeSource _timeSource;

        private bool _isServerTimeSynced;
        private DateTime _serverTimeUtcAtSync;
        private double _monotonicSecondsAtSync;

        public TimeSystem() : this(SystemTimeSource.Instance) { }

        internal TimeSystem(ITimeSource timeSource)
        {
            _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));
        }

        public void OnUpdate(float deltaTime)
        {
            UpdateTimers(UnityTime.deltaTime, UnityTime.unscaledDeltaTime);
        }

        #region Time Properties

        public float GameTime => UnityTime.time;
        public float RealTime => UnityTime.realtimeSinceStartup;
        public float DeltaTime => UnityTime.deltaTime;
        public float UnscaledDeltaTime => UnityTime.unscaledDeltaTime;
        public int FrameCount => UnityTime.frameCount;

        public float TimeScale
        {
            get => UnityTime.timeScale;
            set => UnityTime.timeScale = Mathf.Clamp(value, 0f, 100f);
        }

        #endregion

        #region Server Time

        public DateTime ServerTimeUtc
        {
            get
            {
                if (!_isServerTimeSynced)
                    return NormalizeUtc(_timeSource.UtcNow);

                var elapsedSeconds = Math.Max(0d, _timeSource.MonotonicSeconds - _monotonicSecondsAtSync);
                return _serverTimeUtcAtSync.AddSeconds(elapsedSeconds);
            }
        }

        public long ServerTimeSeconds => new DateTimeOffset(ServerTimeUtc).ToUnixTimeSeconds();
        public bool IsServerTimeSynced => _isServerTimeSynced;

        public void SyncServerTime(DateTime serverTimeUtc)
        {
            _serverTimeUtcAtSync = NormalizeUtc(serverTimeUtc);
            _monotonicSecondsAtSync = _timeSource.MonotonicSeconds;
            _isServerTimeSynced = true;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        #endregion

        #region Timer

        public int ScheduleOnce(float delay, Action callback, bool useRealTime = false)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (delay < 0) delay = 0;

            var timerId = _nextTimerId++;
            _activeTimerIds.Add(timerId);

            lock (_timerLock)
            {
                var timer = TimerInfo.Rent();
                timer.Id = timerId;
                timer.Interval = delay;
                timer.RemainingTime = delay;
                timer.Callback = callback;
                timer.UseRealTime = useRealTime;
                timer.IsRepeat = false;
                timer.RemainingRepeatCount = 1;
                _timers[timerId] = timer;
            }

            return timerId;
        }

        public int ScheduleRepeat(float interval, Action callback, bool useRealTime = false, int repeatCount = -1)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (interval <= 0) interval = 0.001f;

            var timerId = _nextTimerId++;
            _activeTimerIds.Add(timerId);

            lock (_timerLock)
            {
                var timer = TimerInfo.Rent();
                timer.Id = timerId;
                timer.Interval = interval;
                timer.RemainingTime = interval;
                timer.Callback = callback;
                timer.UseRealTime = useRealTime;
                timer.IsRepeat = true;
                timer.RemainingRepeatCount = repeatCount;
                _timers[timerId] = timer;
            }

            return timerId;
        }

        public bool CancelTimer(int timerId)
        {
            lock (_timerLock)
            {
                if (_timers.TryGetValue(timerId, out var timer))
                {
                    timer.IsCancelled = true;
                    _activeTimerIds.Remove(timerId);
                    return true;
                }
                return false;
            }
        }

        public void CancelAllTimers()
        {
            lock (_timerLock)
            {
                foreach (var kvp in _timers)
                {
                    kvp.Value.IsCancelled = true;
                    TimerInfo.Return(kvp.Value);
                }
                _timers.Clear();
            }
            _activeTimerIds.Clear();
        }

        public bool PauseTimer(int timerId)
        {
            lock (_timerLock)
            {
                if (_timers.TryGetValue(timerId, out var timer) && !timer.IsCancelled)
                {
                    timer.IsPaused = true;
                    return true;
                }
                return false;
            }
        }

        public bool ResumeTimer(int timerId)
        {
            lock (_timerLock)
            {
                if (_timers.TryGetValue(timerId, out var timer) && !timer.IsCancelled)
                {
                    timer.IsPaused = false;
                    return true;
                }
                return false;
            }
        }

        private void UpdateTimers(float deltaTime, float unscaledDeltaTime)
        {
            _snapshot.Clear();
            _timersToRemove.Clear();

            lock (_timerLock)
            {
                foreach (var kvp in _timers)
                    _snapshot.Add(kvp.Value);
            }

            for (int i = 0, count = _snapshot.Count; i < count; i++)
            {
                var timer = _snapshot[i];

                if (timer.IsCancelled)
                {
                    _timersToRemove.Add(timer.Id);
                    continue;
                }

                if (timer.IsPaused) continue;

                var dt = timer.UseRealTime ? unscaledDeltaTime : deltaTime;
                timer.RemainingTime -= dt;

                if (timer.RemainingTime > 0) continue;

                try { timer.Callback?.Invoke(); }
                catch (Exception ex) { JLogger.LogException(ex); }

                if (timer.IsRepeat)
                {
                    if (timer.RemainingRepeatCount > 0)
                        timer.RemainingRepeatCount--;

                    if (timer.RemainingRepeatCount == 0)
                        _timersToRemove.Add(timer.Id);
                    else
                        timer.RemainingTime += timer.Interval;
                }
                else
                {
                    _timersToRemove.Add(timer.Id);
                }
            }

            if (_timersToRemove.Count > 0)
            {
                lock (_timerLock)
                {
                    for (int i = 0, count = _timersToRemove.Count; i < count; i++)
                    {
                        var id = _timersToRemove[i];
                        if (_timers.Remove(id, out var removed))
                            TimerInfo.Return(removed);
                    }
                }
            }
        }

        #endregion

        #region Internal

        private class TimerInfo
        {
            private static readonly Stack<TimerInfo> Pool = new(32);

            public int Id;
            public float Interval;
            public float RemainingTime;
            public Action Callback;
            public bool UseRealTime;
            public bool IsRepeat;
            public int RemainingRepeatCount;
            public bool IsPaused;
            public bool IsCancelled;

            public static TimerInfo Rent() => Pool.Count > 0 ? Pool.Pop() : new TimerInfo();

            public static void Return(TimerInfo info)
            {
                if (info == null) return;
                info.Id = 0;
                info.Interval = 0f;
                info.RemainingTime = 0f;
                info.Callback = null;
                info.UseRealTime = false;
                info.IsRepeat = false;
                info.RemainingRepeatCount = 0;
                info.IsPaused = false;
                info.IsCancelled = false;
                Pool.Push(info);
            }
        }

        #endregion
    }

    internal interface ITimeSource
    {
        DateTime UtcNow { get; }
        double MonotonicSeconds { get; }
    }

    internal sealed class SystemTimeSource : ITimeSource
    {
        public static readonly SystemTimeSource Instance = new();

        private SystemTimeSource() { }

        public DateTime UtcNow => DateTime.UtcNow;
        public double MonotonicSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }
}
