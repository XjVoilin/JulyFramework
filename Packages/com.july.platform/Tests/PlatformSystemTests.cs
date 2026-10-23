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

        [TestCase(0, 1.8633899812498247d)]
        [TestCase(1, 2.282177322938192d)]
        [TestCase(2, 2.700308624336608d)]
        public void DprPolicy_TierSelectsConfiguredPixelBudget(int tier, double expected)
        {
            var dpr = DevicePixelRatioPolicy.Calculate(new DprConfig(), (DevicePerformanceTier)tier, 3d, 360d, 800d);
            Assert.That(dpr, Is.EqualTo(expected).Within(0.0001d));
        }

        [Test]
        public void DprPolicy_CustomBudgetChangesResolution()
        {
            var config = new DprConfig { LowTierPixels = 648000 };
            var dpr = DevicePixelRatioPolicy.Calculate(config, DevicePerformanceTier.Low, 3d, 360d, 800d);
            Assert.That(dpr, Is.EqualTo(1.5d).Within(0.0001d));
        }

        [Test]
        public void DprPolicy_TallWindowBelowPixelBudget_IsNotReduced()
        {
            // 600x3000 contains 1.8 million pixels, below the high-tier 2.1 million budget.
            var dpr = DevicePixelRatioPolicy.Calculate(new DprConfig(), DevicePerformanceTier.High, 3d, 200d, 1000d);
            Assert.That(dpr, Is.EqualTo(3d));
        }

        [Test]
        public void DprPolicy_Tablet_IsLimitedByPixelArea()
        {
            var dpr = DevicePixelRatioPolicy.Calculate(new DprConfig(), DevicePerformanceTier.Low, 1.4d, 820d, 1180d);
            Assert.That(820d * 1180d * dpr * dpr, Is.EqualTo(1000000d).Within(0.01d));
        }

        [Test]
        public void DprPolicy_InvalidWindowSize_FailsFast()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DevicePixelRatioPolicy.Calculate(new DprConfig(), DevicePerformanceTier.High, 3d, 0d, 800d));
        }

        [Test]
        public void DprPolicy_BudgetBelowLogicalArea_PreservesMinimumDpr()
        {
            var dpr = DevicePixelRatioPolicy.Calculate(new DprConfig(), DevicePerformanceTier.Low, 3d, 1920d, 1080d);
            Assert.That(dpr, Is.EqualTo(1d));
        }

        [Test]
        public void DprPolicy_NativeDprBelowOne_IsNotIncreased()
        {
            var dpr = DevicePixelRatioPolicy.Calculate(new DprConfig(), DevicePerformanceTier.High, 0.5d, 360d, 800d);
            Assert.That(dpr, Is.EqualTo(0.5d));
        }

        [Test]
        public void DprPolicy_Disabled_PreservesDefaultAndIgnoresUnusedBudget()
        {
            var config = new DprConfig { EnablePixelBudget = false, HighTierPixels = 0 };
            var dpr = DevicePixelRatioPolicy.Calculate(config, DevicePerformanceTier.High, 3d, 360d, 800d);
            Assert.That(dpr, Is.EqualTo(3d));
        }

        [Test]
        public void DprPolicy_OnlySelectedTierBudgetIsValidated()
        {
            var config = new DprConfig { HighTierPixels = 0 };
            Assert.DoesNotThrow(() =>
                DevicePixelRatioPolicy.Calculate(config, DevicePerformanceTier.Low, 3d, 360d, 800d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DevicePixelRatioPolicy.Calculate(config, DevicePerformanceTier.High, 3d, 360d, 800d));
        }
    }
}
