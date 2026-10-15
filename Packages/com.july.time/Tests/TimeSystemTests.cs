using System;
using System.Linq;
using NUnit.Framework;

namespace July.Time.Tests
{
    [TestFixture]
    public sealed class TimeSystemTests
    {
        [Test]
        public void PublicInterface_OnlyExposesClockAndTimerCapabilities()
        {
            var propertyNames = typeof(ITimeSystem)
                .GetProperties()
                .Select(property => property.Name);
            var methodNames = typeof(ITimeSystem)
                .GetMethods()
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name);

            Assert.That(propertyNames, Is.EquivalentTo(new[]
            {
                nameof(ITimeSystem.GameTime),
                nameof(ITimeSystem.RealTime),
                nameof(ITimeSystem.DeltaTime),
                nameof(ITimeSystem.FixedDeltaTime),
                nameof(ITimeSystem.UnscaledDeltaTime),
                nameof(ITimeSystem.FrameCount),
                nameof(ITimeSystem.TimeScale),
                nameof(ITimeSystem.ServerTimeUtc),
                nameof(ITimeSystem.ServerTimeSeconds),
                nameof(ITimeSystem.IsServerTimeSynced)
            }));
            Assert.That(methodNames, Is.EquivalentTo(new[]
            {
                nameof(ITimeSystem.SyncServerTime),
                nameof(ITimeSystem.ScheduleOnce),
                nameof(ITimeSystem.ScheduleRepeat),
                nameof(ITimeSystem.TryGetTimerRemainingTime),
                nameof(ITimeSystem.CancelTimer),
                nameof(ITimeSystem.CancelAllTimers),
                nameof(ITimeSystem.PauseTimer),
                nameof(ITimeSystem.ResumeTimer)
            }));
        }

        [Test]
        public void RemainingTime_FollowsAdvanceAndPause_ThenExpires()
        {
            var system = new TimeSystem();
            var fired = 0;
            var id = 0;
            id = system.ScheduleOnce(2f, () =>
            {
                fired++;
                Assert.That(system.TryGetTimerRemainingTime(id, out var duringCallback), Is.True);
                Assert.That(duringCallback, Is.Zero);
            });
            AdvanceTimers(system, .5f, .5f);
            Assert.That(system.TryGetTimerRemainingTime(id, out var remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(1.5f));
            system.PauseTimer(id);
            AdvanceTimers(system, 10f, 10f);
            system.TryGetTimerRemainingTime(id, out remaining);
            Assert.That(remaining, Is.EqualTo(1.5f));
            system.ResumeTimer(id);
            AdvanceTimers(system, 2f, 2f);
            Assert.That(fired, Is.EqualTo(1));
            Assert.That(system.TryGetTimerRemainingTime(id, out _), Is.False);
            AdvanceTimers(system, 2f, 2f);
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void RemainingTime_RejectsCancelledAndUnknownTimers()
        {
            var system = new TimeSystem();
            var id = system.ScheduleOnce(10f, () => Assert.Fail("Cancelled timer fired."));
            system.CancelTimer(id);
            Assert.That(system.TryGetTimerRemainingTime(id, out _), Is.False);
            Assert.That(system.TryGetTimerRemainingTime(-1, out _), Is.False);
            var other = system.ScheduleOnce(10f, () => { });
            system.CancelAllTimers();
            Assert.That(system.TryGetTimerRemainingTime(other, out _), Is.False);
        }

        [Test]
        public void RemainingTime_ReportsNextRepeat_AndRespectsClockChoice()
        {
            var system = new TimeSystem();
            var scaled = system.ScheduleRepeat(2f, () => { });
            var real = system.ScheduleOnce(2f, () => { }, useRealTime: true);
            AdvanceTimers(system, 0f, .5f);
            system.TryGetTimerRemainingTime(scaled, out var scaledRemaining);
            system.TryGetTimerRemainingTime(real, out var realRemaining);
            Assert.That(scaledRemaining, Is.EqualTo(2f));
            Assert.That(realRemaining, Is.EqualTo(1.5f));
            AdvanceTimers(system, 2.25f, 0f);
            system.TryGetTimerRemainingTime(scaled, out scaledRemaining);
            Assert.That(scaledRemaining, Is.EqualTo(1.75f));
        }

        private static void AdvanceTimers(TimeSystem system, float delta, float unscaledDelta)
            => typeof(TimeSystem).GetMethod("UpdateTimers", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(system, new object[] { delta, unscaledDelta });

        [Test]
        public void FixedDeltaTime_FollowsUnityFixedStepChanges()
        {
            ITimeSystem system = new TimeSystem();
            var previous = UnityEngine.Time.fixedDeltaTime;
            try
            {
                UnityEngine.Time.fixedDeltaTime = 0.02f;
                Assert.That(system.FixedDeltaTime, Is.EqualTo(0.02f));
                UnityEngine.Time.fixedDeltaTime = 0.01f;
                Assert.That(system.FixedDeltaTime, Is.EqualTo(0.01f));
            }
            finally { UnityEngine.Time.fixedDeltaTime = previous; }
        }

        [Test]
        public void SyncServerTime_UpdatesSyncStateAndUnixSeconds()
        {
            var localUtc = new DateTime(2026, 8, 4, 2, 0, 0, DateTimeKind.Utc);
            var serverUtc = DateTimeOffset.FromUnixTimeSeconds(1785809100).UtcDateTime;
            var timeSource = new FakeTimeSource(localUtc, 100d);
            var system = new TimeSystem(timeSource);

            Assert.That(system.IsServerTimeSynced, Is.False);

            system.SyncServerTime(serverUtc);
            timeSource.MonotonicSeconds = 102d;

            Assert.That(system.IsServerTimeSynced, Is.True);
            Assert.That(system.ServerTimeSeconds, Is.EqualTo(1785809102));
        }

        [Test]
        public void ServerTimeUtc_AdvancesByMonotonicTime_NotWallClock()
        {
            var localUtc = new DateTime(2026, 8, 4, 2, 0, 0, DateTimeKind.Utc);
            var serverUtc = localUtc.AddMinutes(5);
            var timeSource = new FakeTimeSource(localUtc, 100d);
            var system = new TimeSystem(timeSource);
            system.SyncServerTime(serverUtc);

            timeSource.UtcNow = localUtc.AddHours(3);
            timeSource.MonotonicSeconds = 112.5d;

            Assert.That(system.ServerTimeUtc, Is.EqualTo(serverUtc.AddSeconds(12.5d)));
        }

        [Test]
        public void SyncServerTime_NormalizesLocalInputToUtc()
        {
            var localUtc = new DateTime(2026, 8, 4, 2, 0, 0, DateTimeKind.Utc);
            var expectedServerUtc = localUtc.AddMinutes(5);
            var timeSource = new FakeTimeSource(localUtc, 100d);
            var system = new TimeSystem(timeSource);

            system.SyncServerTime(expectedServerUtc.ToLocalTime());

            Assert.That(system.ServerTimeUtc, Is.EqualTo(expectedServerUtc));
            Assert.That(system.ServerTimeUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void SyncServerTime_TreatsUnspecifiedInputAsUtcByContract()
        {
            var localUtc = new DateTime(2026, 8, 4, 2, 0, 0, DateTimeKind.Utc);
            var unspecifiedServerTime = DateTime.SpecifyKind(localUtc.AddMinutes(5), DateTimeKind.Unspecified);
            var timeSource = new FakeTimeSource(localUtc, 100d);
            var system = new TimeSystem(timeSource);

            system.SyncServerTime(unspecifiedServerTime);

            Assert.That(system.ServerTimeUtc, Is.EqualTo(DateTime.SpecifyKind(unspecifiedServerTime, DateTimeKind.Utc)));
            Assert.That(system.ServerTimeUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void RepeatedSync_ReplacesBothServerAndMonotonicBaselines()
        {
            var localUtc = new DateTime(2026, 8, 4, 2, 0, 0, DateTimeKind.Utc);
            var timeSource = new FakeTimeSource(localUtc, 100d);
            var system = new TimeSystem(timeSource);
            system.SyncServerTime(localUtc.AddMinutes(5));

            timeSource.UtcNow = localUtc.AddSeconds(20);
            timeSource.MonotonicSeconds = 120d;
            var resyncedServerUtc = localUtc.AddMinutes(8);
            system.SyncServerTime(resyncedServerUtc);
            timeSource.UtcNow = localUtc.AddHours(-2);
            timeSource.MonotonicSeconds = 123d;

            Assert.That(system.ServerTimeUtc, Is.EqualTo(resyncedServerUtc.AddSeconds(3)));
        }

        private sealed class FakeTimeSource : ITimeSource
        {
            public FakeTimeSource(DateTime utcNow, double monotonicSeconds)
            {
                UtcNow = utcNow;
                MonotonicSeconds = monotonicSeconds;
            }

            public DateTime UtcNow { get; set; }
            public double MonotonicSeconds { get; set; }
        }
    }
}
