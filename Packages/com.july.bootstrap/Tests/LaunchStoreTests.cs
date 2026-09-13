using System;
using System.Threading;
using July.Arch;
using July.Release;
using NUnit.Framework;
using UnityEngine;

namespace July.Bootstrap.Tests
{
    public class LaunchStoreTests
    {
        private LaunchStoreTestConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<LaunchStoreTestConfig>();

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_config);

        [Test]
        public void ProjectConfigIsAvailableBeforeStartupResultAndIsNotCopied()
        {
            var store = new LaunchStore(_config);
            Assert.That(store.GetProjectConfig<LaunchStoreTestConfig>(), Is.SameAs(_config));
            _config.Value = 42;
            Assert.That(store.GetProjectConfig<LaunchStoreTestConfig>().Value, Is.EqualTo(42));
            Assert.Throws<InvalidOperationException>(() => { var result = store.Current; });
        }

        [Test]
        public void MissingOrDestroyedConfigFailsAtConstruction()
        {
            Assert.Throws<ArgumentNullException>(() => new LaunchStore(null));
            UnityEngine.Object.DestroyImmediate(_config);
            Assert.Throws<ArgumentNullException>(() => new LaunchStore(_config));
        }

        [Test]
        public void WrongProjectConfigTypeFailsExplicitly()
        {
            var store = new LaunchStore(_config);
            Assert.Throws<InvalidCastException>(() => store.GetProjectConfig<OtherLaunchStoreTestConfig>());
        }

        [Test]
        public void SuccessfulRetryReplacesOnlyTheResult()
        {
            var store = new LaunchStore(_config);
            var first = new LaunchInfo(ReleaseEnvironment.Dev, "first", "1", false, "cdn-1");
            store.SetResult(first);
            var earlierRead = store.Current;
            var next = new LaunchInfo(ReleaseEnvironment.Prod, "second", "2", true, "cdn-2");
            store.SetResult(next);
            Assert.That(store.Current, Is.SameAs(next));
            Assert.That(store.Current.IsDev, Is.False);
            Assert.That(store.Current.ServerUrl, Is.EqualTo("second"));
            Assert.That(store.Current.PlanVersion, Is.EqualTo("2"));
            Assert.That(store.Current.IsAudit, Is.True);
            Assert.That(store.Current.RemoteUrl, Is.EqualTo("cdn-2"));
            Assert.That(earlierRead.PlanVersion, Is.EqualTo("1"));
            Assert.That(earlierRead.IsDev, Is.True);
            Assert.That(store.GetProjectConfig<LaunchStoreTestConfig>(), Is.SameAs(_config));
        }

        [Test]
        public void CancelledConfigurationAttemptDoesNotPublishAResult()
        {
            var arch = new ArchContext();
            try
            {
                var store = new LaunchStore(_config);
                arch.RegisterStore(store);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                var step = new FetchConfigStep(new DeploymentConfig(), YooAsset.EPlayMode.EditorSimulateMode, July.Logging.LogChannel.All);
                var task = step.ExecuteAsync(cancellation.Token);
                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                Assert.Throws<InvalidOperationException>(() => { var result = store.Current; });
                Assert.That(store.GetProjectConfig<LaunchStoreTestConfig>(), Is.SameAs(_config));
            }
            finally { arch.Shutdown(); }
        }

        [Test]
        public void ArchitectureShutdownKeepsProjectOwnedConfigAndNextLaunchStartsEmpty()
        {
            var arch = new ArchContext();
            try
            {
                var store = new LaunchStore(_config);
                arch.RegisterStore(store);
                store.SetResult(new LaunchInfo(ReleaseEnvironment.Dev, "first", "1", false, "cdn-1"));
            }
            finally { arch.Shutdown(); }
            Assert.That(_config != null, Is.True);
            var nextArch = new ArchContext();
            try
            {
                var next = new LaunchStore(_config);
                nextArch.RegisterStore(next);
                Assert.That(nextArch.GetStore<LaunchStore>(), Is.SameAs(next));
                Assert.Throws<InvalidOperationException>(() => { var result = next.Current; });
            }
            finally { nextArch.Shutdown(); }
        }
    }

    public sealed class LaunchStoreTestConfig : ScriptableObject
    {
        public int Value;
    }

    public sealed class OtherLaunchStoreTestConfig : ScriptableObject { }
}
