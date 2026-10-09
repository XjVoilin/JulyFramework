using System.Collections.Generic;
using July.Arch;
using NUnit.Framework;

namespace July.Platform.Tests
{
    [TestFixture]
    public sealed class AccelerometerServiceTests
    {
        [Test]
        public void DefaultAdapter_RegistersAccelerometerWithoutStartingIt()
        {
            var context = new ArchContext();
            try
            {
                var results = new List<AccelerometerStartResultEvent>();
                context.Event.Subscribe<AccelerometerStartResultEvent>(results.Add, this);
                var registry = new PlatformServiceRegistry();
                new DefaultPlatformAdapter().ConfigureAsync(registry, default).GetAwaiter().GetResult();
                Assert.That(registry.Get<IAccelerometerService>(), Is.TypeOf<DefaultAccelerometerService>());
                Assert.That(results, Is.Empty);
            }
            finally
            {
                context.Shutdown();
            }
        }

        [Test]
        public void DefaultStart_PublishesUnsupportedResultWithoutSamples()
        {
            var context = new ArchContext();
            try
            {
                var results = new List<AccelerometerStartResultEvent>();
                var samples = new List<AccelerometerSampleEvent>();
                context.Event.Subscribe<AccelerometerStartResultEvent>(results.Add, this);
                context.Event.Subscribe<AccelerometerSampleEvent>(samples.Add, this);
                IAccelerometerService service = new DefaultAccelerometerService();
                service.Start();
                Assert.That(results.Count, Is.EqualTo(1));
                Assert.That(results[0].IsSuccess, Is.False);
                StringAssert.Contains("not supported", results[0].Error);
                Assert.That(samples, Is.Empty);
            }
            finally
            {
                context.Shutdown();
            }
        }

        [Test]
        public void DefaultStop_PublishesStopResultOnly()
        {
            var context = new ArchContext();
            try
            {
                var results = new List<AccelerometerStopResultEvent>();
                var starts = new List<AccelerometerStartResultEvent>();
                context.Event.Subscribe<AccelerometerStopResultEvent>(results.Add, this);
                context.Event.Subscribe<AccelerometerStartResultEvent>(starts.Add, this);
                IAccelerometerService service = new DefaultAccelerometerService();
                service.Stop();
                Assert.That(results.Count, Is.EqualTo(1));
                Assert.That(results[0].IsSuccess, Is.False);
                StringAssert.Contains("not supported", results[0].Error);
                Assert.That(starts, Is.Empty);
            }
            finally
            {
                context.Shutdown();
            }
        }
    }
}
