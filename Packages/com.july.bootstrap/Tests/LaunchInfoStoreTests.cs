using System;
using System.Threading;
using July.Arch;
using July.Release;
using NUnit.Framework;

namespace July.Bootstrap.Tests
{
    public class LaunchInfoStoreTests
    {
        [Test]
        public void ReadingBeforeConfigurationSucceedsFailsExplicitly()
        {
            var store = new LaunchInfoStore();
            Assert.Throws<InvalidOperationException>(() => { var result = store.Current; });
        }

        [Test]
        public void SuccessfulRetryReplacesWholeResultWithoutChangingEarlierReaders()
        {
            var store = new LaunchInfoStore();
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
        }

        [Test]
        public void CancelledConfigurationAttemptDoesNotPublishAResult()
        {
            var arch = new ArchContext();
            try
            {
                var store = new LaunchInfoStore();
                arch.RegisterStore(store);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                var step = new FetchConfigStep(new ReleaseSettings(), YooAsset.EPlayMode.EditorSimulateMode, July.Logging.LogChannel.All);
                var task = step.ExecuteAsync(cancellation.Token);
                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                Assert.Throws<InvalidOperationException>(() => { var result = store.Current; });
            }
            finally { arch.Shutdown(); }
        }
    }
}