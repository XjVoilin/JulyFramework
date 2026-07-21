using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using NUnit.Framework;

namespace July.Platform.Tests
{
    [TestFixture]
    public sealed class PlatformSystemTests
    {
        private interface IFirstService : IPlatformService { }
        private interface ISecondService : IPlatformService { }

        private sealed class RecordingService : IFirstService, INeedGetService
        {
            private readonly List<string> _events;
            public Func<Type, object> ServiceGetter { get; set; }
            public bool ResolvedSecondService { get; private set; }

            public RecordingService(List<string> events) => _events = events;

            public void Init() => _events.Add("first.init");

            public void PostInit()
            {
                ResolvedSecondService = this.GetService<ISecondService>() != null;
                _events.Add("first.post");
            }

            public UniTask PostInitAsync()
            {
                _events.Add("first.async");
                return UniTask.CompletedTask;
            }

            public void DeferredInit() => _events.Add("first.defer");
            public void Shutdown() => _events.Add("first.shutdown");
        }

        private sealed class SecondService : ISecondService
        {
            private readonly List<string> _events;
            public SecondService(List<string> events) => _events = events;
            public void Init() => _events.Add("second.init");
            public void PostInit() => _events.Add("second.post");
            public UniTask PostInitAsync()
            {
                _events.Add("second.async");
                return UniTask.CompletedTask;
            }
            public void DeferredInit() => _events.Add("second.defer");
            public void Shutdown() => _events.Add("second.shutdown");
        }

        private sealed class RecordingAdapter : IPlatformAdapter
        {
            private readonly List<string> _events;
            public RecordingService First { get; }
            public int PlatformType => 42;

            public RecordingAdapter(List<string> events)
            {
                _events = events;
                First = new RecordingService(events);
            }

            public UniTask ConfigureAsync(
                PlatformServiceRegistry registry,
                CancellationToken cancellationToken)
            {
                _events.Add("adapter.configure");
                registry.Register<IFirstService>(First);
                registry.Register<ISecondService>(new SecondService(_events));
                return UniTask.CompletedTask;
            }

            public void VibrateShort(VibrateType type) => _events.Add($"adapter.short:{type}");
            public void VibrateLong() => _events.Add("adapter.long");
            public void Shutdown() => _events.Add("adapter.shutdown");
        }

        [Test]
        public void Initialize_UsesGooseMarketLifecycleOrderingAndServiceResolution()
        {
            var events = new List<string>();
            var adapter = new RecordingAdapter(events);
            var platform = new PlatformSystem(adapter);
            var context = new ArchContext();

            context.RegisterSystem(platform);
            context.InitializeAsync().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[]
            {
                "adapter.configure",
                "first.init",
                "second.init",
                "first.post",
                "second.post",
                "first.async",
                "second.async"
            }, events);
            Assert.That(adapter.First.ResolvedSecondService, Is.True);
            Assert.That(context.GetSystem<IPlatformSystem>(), Is.SameAs(platform));
            Assert.That(platform.GetService<IFirstService>(), Is.SameAs(adapter.First));

            context.Shutdown();
        }

        [Test]
        public void DeferVibrateAndShutdown_AreOwnedByPlatformSystem()
        {
            var events = new List<string>();
            var adapter = new RecordingAdapter(events);
            var platform = new PlatformSystem(adapter);
            var context = new ArchContext();
            context.RegisterSystem(platform);
            context.InitializeAsync().GetAwaiter().GetResult();
            events.Clear();

            platform.DeferAllServices();
            platform.VibrateShort(VibrateType.Heavy);
            platform.VibrateLong();
            context.Shutdown();

            CollectionAssert.AreEqual(new[]
            {
                "first.defer",
                "second.defer",
                "adapter.short:Heavy",
                "adapter.long",
                "second.shutdown",
                "first.shutdown",
                "adapter.shutdown"
            }, events);
        }

        [Test]
        public void Registry_DuplicateContract_ThrowsUnlessReplaceIsExplicit()
        {
            var registry = new PlatformServiceRegistry();
            var events = new List<string>();
            registry.Register<IFirstService>(new RecordingService(events));

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register<IFirstService>(new RecordingService(events)));
            Assert.DoesNotThrow(() =>
                registry.Register<IFirstService>(new RecordingService(events), replace: true));
        }
    }
}
