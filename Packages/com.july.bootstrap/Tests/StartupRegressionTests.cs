using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Launch;
using July.Platform;
using July.Release;
using LitJson;
using NUnit.Framework;

namespace July.Bootstrap.Tests
{
    public class StartupRegressionTests
    {
        [Test]
        public void ParallelReadsApplyInDeclarationOrderAndReleaseEveryHandle()
        {
            var a = new UniTaskCompletionSource<Handle>();
            var b = new UniTaskCompletionSource<Handle>();
            var reads = new List<string>();
            var applied = new List<int>();
            var first = new Handle();
            var second = new Handle();
            var task = BootstrapAssemblyLoader.ReadAndApplyBatchAsync(new[] { "a", "b" },
                (name, _) => { reads.Add(name); return name == "a" ? a.Task : b.Task; },
                (_, index) => applied.Add(index), default);
            CollectionAssert.AreEqual(new[] { "a", "b" }, reads);
            b.TrySetResult(second);
            Assert.That(applied, Is.Empty);
            a.TrySetResult(first);
            task.GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { 0, 1 }, applied);
            Assert.That(first.Releases, Is.EqualTo(1));
            Assert.That(second.Releases, Is.EqualTo(1));
        }

        [Test]
        public void FailedReadDrainsSiblingsAndReleasesTheirLateHandles()
        {
            var a = new UniTaskCompletionSource<Handle>();
            var b = new UniTaskCompletionSource<Handle>();
            var late = new Handle();
            var error = new InvalidOperationException("read failed");
            var task = BootstrapAssemblyLoader.ReadAndApplyBatchAsync(new[] { "a", "b" },
                (name, _) => name == "a" ? a.Task : b.Task,
                (_, _) => Assert.Fail("Must not apply incomplete metadata"), default);
            a.TrySetException(error);
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending));
            b.TrySetResult(late);
            Assert.That(Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()), Is.SameAs(error));
            Assert.That(late.Releases, Is.EqualTo(1));
        }

        [Test]
        public void CancellationDrainsReadsWithoutApplyingAndReleasesHandles()
        {
            using var ct = new CancellationTokenSource();
            var pending = new UniTaskCompletionSource<Handle>();
            var handle = new Handle();
            var task = BootstrapAssemblyLoader.ReadAndApplyBatchAsync(new[] { "a" }, (_, _) => pending.Task,
                (_, _) => Assert.Fail("Cancelled metadata applied"), ct.Token);
            ct.Cancel();
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending));
            pending.TrySetResult(handle);
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.That(handle.Releases, Is.EqualTo(1));
        }

        [Test]
        public void FailedMetadataApplicationReleasesUnreadHandlesToo()
        {
            var a = new Handle();
            var b = new Handle();
            var task = BootstrapAssemblyLoader.ReadAndApplyBatchAsync(new[] { "a", "b" },
                (name, _) => UniTask.FromResult(name == "a" ? a : b),
                (_, _) => throw new InvalidOperationException("metadata rejected"), default);
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.That(a.Releases, Is.EqualTo(1));
            Assert.That(b.Releases, Is.EqualTo(1));
        }

        [Test]
        public void PrefetchRequiresMatchingRequestIdentity()
        {
            var cache = new JsonData
            {
                ["requestUrl"] = "https://config.example/client_version", ["environment"] = "Dev",
                ["platform"] = "WeChat", ["coreVersion"] = "1.0", ["responseJson"] = "{\"ok\":true}"
            };
            Assert.That(ReadCache(cache.ToJson(), out var response), Is.True);
            Assert.That(response, Is.EqualTo("{\"ok\":true}"));
            foreach (var field in new[] { "requestUrl", "environment", "platform", "coreVersion" })
            {
                var original = (string)cache[field];
                cache[field] = "different";
                Assert.That(ReadCache(cache.ToJson(), out _), Is.False, field);
                cache[field] = original;
            }
            foreach (var invalid in new[] { "", "{", "null", "[]", "{\"serverUrl\":\"old raw cache\"}" })
                Assert.That(ReadCache(invalid, out _), Is.False, invalid);
            cache["coreVersion"] = 1;
            Assert.That(ReadCache(cache.ToJson(), out _), Is.False);
        }

        [Test]
        public void StartupTagsSupportMableWithoutLobbyAndDeduplicateBuildTags()
        {
            var resource = new ReleaseResourceSettings
            {
                StartupDownloadTags = new[] { "Startup", "HotFix", "Startup" }
            };
            CollectionAssert.AreEqual(new[] { "AotMeta", "HotFix", "Startup" }, resource.RequiredDownloadTags);
            resource.StartupDownloadTags = new[] { "Lobby" };
            CollectionAssert.AreEqual(new[] { "AotMeta", "HotFix", "Lobby" }, resource.RequiredDownloadTags);
        }

        [Test]
        public void PlatformDeferredInitializationRunsAfterGameEntryBeforeCompletion()
        {
            var arch = new ArchContext();
            try
            {
                var platform = new TestPlatform();
                arch.RegisterSystem(platform);
                var pending = new UniTaskCompletionSource();
                var view = new RegistrarStepsTests.TestView();
                var registrar = new RegistrarStepsTests.TestRegistrar { Launch = pending.Task };
                var pipeline = new LaunchPipeline { OnCompleted = view.Complete };
                pipeline.Add(new LaunchGameStep(new AppRegistration { Registrar = registrar }, view));
                pipeline.Add(new InitializeDeferredServicesStep());
                var task = pipeline.ExecuteAsync(default);
                Assert.That(platform.Deferred, Is.Zero);
                Assert.That(view.Completed, Is.False);
                pending.TrySetResult();
                Assert.That(task.GetAwaiter().GetResult(), Is.True);
                Assert.That(platform.Deferred, Is.EqualTo(1));
                Assert.That(view.Completed, Is.True);
            }
            finally { arch.Shutdown(); }
        }

        [Test]
        public void ArchitectureCancellationReachesPlatformInitialization()
        {
            var arch = new ArchContext();
            using var ct = new CancellationTokenSource();
            try
            {
                var adapter = new PendingAdapter();
                arch.RegisterSystem(new PlatformSystem(adapter));
                var task = arch.InitializeAsync(ct.Token);
                ct.Cancel();
                Assert.That(adapter.Observed, Is.True);
                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            }
            finally { arch.Shutdown(); }
        }

        private static bool ReadCache(string json, out string response) => ClientVersionProtocol.TryReadPrefetch(json,
            "https://config.example/client_version", "Dev", "WeChat", "1.0", out response);
        private sealed class Handle : IDisposable
        {
            internal int Releases;
            public void Dispose() => Releases++;
        }
        private sealed class TestPlatform : SystemBase, IPlatformSystem
        {
            internal int Deferred;
            public int PlatformType => 1;
            public T GetService<T>() where T : class => null;
            public void DeferAllServices() => Deferred++;
            public void VibrateShort(VibrateType type = VibrateType.Light) { }
            public void VibrateLong() { }
        }
        private sealed class PendingAdapter : IPlatformAdapter
        {
            internal bool Observed;
            public int PlatformType => 1;
            public async UniTask ConfigureAsync(PlatformServiceRegistry registry, CancellationToken ct)
            {
                var pending = new UniTaskCompletionSource();
                using var cancellation = ct.Register(() => { Observed = true; pending.TrySetCanceled(ct); });
                await pending.Task;
            }
            public void VibrateShort(VibrateType type) { }
            public void VibrateLong() { }
            public void Shutdown() { }
        }
    }
}
