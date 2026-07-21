using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;
using UnityEngine;
using UnityEngine.Networking;
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

        private bool _isServerTimeSynced;
        private double _serverTimeOffset;

        private static readonly string[] DefaultNtpServers =
        {
            "time.windows.com",
            "pool.ntp.org",
            "time.google.com",
            "time.apple.com"
        };

        private static readonly string[] DefaultHttpTimeUrls =
        {
            "https://www.baidu.com",
            "https://www.qq.com",
            "https://www.apple.com"
        };

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

        public DateTime ServerTimeUtc => _isServerTimeSynced
            ? DateTime.UtcNow.AddSeconds(_serverTimeOffset)
            : DateTime.UtcNow;

        public DateTime ServerTimeLocal => ServerTimeUtc.ToLocalTime();
        public long ServerTimeSeconds => new DateTimeOffset(ServerTimeUtc).ToUnixTimeSeconds();
        public bool IsServerTimeSynced => _isServerTimeSynced;
        public double ServerTimeOffset => _serverTimeOffset;

        public void SyncServerTime(DateTime serverTimeUtc)
        {
            _serverTimeOffset = (serverTimeUtc - DateTime.UtcNow).TotalSeconds;
            _isServerTimeSynced = true;
        }

        public async UniTask<bool> SyncServerTimeFromNetworkAsync(string ntpServer = null, CancellationToken ct = default)
        {
            var urls = string.IsNullOrEmpty(ntpServer) ? DefaultHttpTimeUrls : new[] { ntpServer };
            foreach (var url in urls)
            {
                if (ct.IsCancellationRequested) return false;
                try
                {
                    var httpTime = await GetHttpTimeAsync(url, ct);
                    if (httpTime.HasValue)
                    {
                        SyncServerTime(httpTime.Value);
                        JLogger.Log($"[TimeSystem] Server time synced via {url}, offset={_serverTimeOffset:F1}s");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    JLogger.LogWarning($"[TimeSystem] HTTP time sync failed from {url}: {ex.Message}");
                }
            }
            JLogger.LogWarning("[TimeSystem] Server time sync failed, all sources exhausted. Falling back to local time.");
            return false;
        }

        private static async UniTask<DateTime?> GetHttpTimeAsync(string url, CancellationToken ct = default)
        {
            using var request = UnityWebRequest.Head(url);
            request.timeout = 5;

            try
            {
                await request.SendWebRequest().WithCancellation(ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
                return null;

            var dateHeader = request.GetResponseHeader("Date");
            if (string.IsNullOrEmpty(dateHeader))
                return null;

            if (DateTime.TryParseExact(dateHeader, "r", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal, out var serverTime))
                return serverTime;

            return null;
        }

        private async UniTask<DateTime?> GetNtpTimeAsync(string ntpServer, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(ntpServer)) return null;

            try
            {
                var ntpData = new byte[48];
                ntpData[0] = 0x1B;

                var addresses = await Dns.GetHostAddressesAsync(ntpServer);
                if (addresses.Length == 0) return null;

                var ipEndPoint = new IPEndPoint(addresses[0], 123);

                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.ReceiveTimeout = 3000;
                socket.SendTimeout = 3000;

                await socket.ConnectAsync(ipEndPoint);
                await socket.SendAsync(new ArraySegment<byte>(ntpData), SocketFlags.None);

                var buffer = new byte[48];
                await socket.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None);

                ulong intPart = (ulong)buffer[40] << 24 | (ulong)buffer[41] << 16 |
                                (ulong)buffer[42] << 8 | buffer[43];
                ulong fractPart = (ulong)buffer[44] << 24 | (ulong)buffer[45] << 16 |
                                  (ulong)buffer[46] << 8 | buffer[47];

                var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);
                var ntpTime = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddMilliseconds((long)milliseconds);

                return ntpTime;
            }
            catch (Exception ex)
            {
                    JLogger.LogWarning($"[TimeSystem] NTP request failed ({ntpServer}): {ex.Message}");
                return null;
            }
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

        #region Formatting

        public string FormatTime(float seconds, string format = null)
        {
            if (seconds < 0) seconds = 0;
            return FormatTimeSpan(TimeSpan.FromSeconds(seconds), format);
        }

        public string FormatTimeSpan(TimeSpan timeSpan, string format = null)
        {
            if (string.IsNullOrEmpty(format))
            {
                if (timeSpan.TotalHours >= 1)
                    return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
                if (timeSpan.TotalMinutes >= 1)
                    return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
                return $"{timeSpan.Seconds:D2}";
            }

            return format
                .Replace("HH", ((int)timeSpan.TotalHours).ToString("D2"))
                .Replace("H", ((int)timeSpan.TotalHours).ToString())
                .Replace("mm", timeSpan.Minutes.ToString("D2"))
                .Replace("m", timeSpan.Minutes.ToString())
                .Replace("ss", timeSpan.Seconds.ToString("D2"))
                .Replace("s", timeSpan.Seconds.ToString())
                .Replace("fff", timeSpan.Milliseconds.ToString("D3"))
                .Replace("ff", (timeSpan.Milliseconds / 10).ToString("D2"))
                .Replace("f", (timeSpan.Milliseconds / 100).ToString());
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
}
