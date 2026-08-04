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
                nameof(ITimeSystem.CancelTimer),
                nameof(ITimeSystem.CancelAllTimers),
                nameof(ITimeSystem.PauseTimer),
                nameof(ITimeSystem.ResumeTimer)
            }));
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
