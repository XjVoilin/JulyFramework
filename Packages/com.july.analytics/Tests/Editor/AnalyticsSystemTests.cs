using System.Collections.Generic;
using July.Arch;
using NUnit.Framework;

namespace July.Analytics.Tests
{
    public class AnalyticsSystemTests
    {
        private ArchContext _context;
        private FakeChannel _first;
        private FakeChannel _second;
        private AnalyticsSystem _system;

        [SetUp]
        public void SetUp()
        {
            _first = new FakeChannel();
            _second = new FakeChannel();
            _system = new AnalyticsSystem(_first, _second);
            _context = new ArchContext();
            _context.RegisterSystem(_system);
            _context.InitializeAsync().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown() => _context.Shutdown();

        [Test]
        public void Track_FansOutToEveryChannel()
        {
            _system.Track("launch", new Dictionary<string, object> { ["version"] = "1" });
            Assert.That(_first.LastEvent, Is.EqualTo("launch"));
            Assert.That(_second.LastEvent, Is.EqualTo("launch"));
        }

        [Test]
        public void Disabled_DropsTrackingEvents()
        {
            _system.SetEnabled(false);
            _system.Track("ignored");
            Assert.That(_first.LastEvent, Is.Null);
            Assert.That(_second.LastEvent, Is.Null);
        }

        [Test]
        public void Lifecycle_IsForwarded()
        {
            Assert.That(_first.InitializeCount, Is.EqualTo(1));
            _context.Shutdown();
            Assert.That(_first.ShutdownCount, Is.EqualTo(1));
        }

        private sealed class FakeChannel : IAnalyticsChannel
        {
            public int InitializeCount;
            public int ShutdownCount;
            public string LastEvent;
            public void Initialize() => InitializeCount++;
            public void Track(string eventName, Dictionary<string, object> parameters) => LastEvent = eventName;
            public void SetUserId(string userId) { }
            public void SetUserProperties(Dictionary<string, object> properties) { }
            public void Flush() { }
            public void SetLogEnabled(bool enabled) { }
            public void Shutdown() => ShutdownCount++;
        }
    }
}
