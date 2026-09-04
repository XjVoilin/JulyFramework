using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace July.Resource.Tests
{
    [TestFixture]
    public sealed class ResourceScopeTests
    {
        private sealed class FirstAsset : ScriptableObject { }
        private sealed class SecondAsset : ScriptableObject { }
        private sealed class TestComponent : MonoBehaviour { }

        private sealed class TestResourceSystem : IResourceSystem
        {
            private readonly Dictionary<Type, Dictionary<string, UnityEngine.Object>> _assets = new();
            private UniTaskCompletionSource _loadGate;

            internal int LoadCallCount { get; private set; }
            internal int ReleaseCallCount { get; private set; }
            internal bool DelayLoads { get; set; }

            internal void Add<T>(string fileName, T asset) where T : UnityEngine.Object
            {
                if (!_assets.TryGetValue(typeof(T), out var typedAssets))
                {
                    typedAssets = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
                    _assets.Add(typeof(T), typedAssets);
                }
                typedAssets.Add(fileName, asset);
            }

            internal void BeginDelayingLoads()
            {
                DelayLoads = true;
                _loadGate = new UniTaskCompletionSource();
            }

            internal void CompleteLoads()
            {
                DelayLoads = false;
                _loadGate.TrySetResult();
            }

            public async UniTask<ResourceHandle<T>> LoadAssetAsync<T>(string fileName,
                CancellationToken ct = default) where T : UnityEngine.Object
            {
                LoadCallCount++;
                if (DelayLoads)
                    await _loadGate.Task;

                if (!_assets.TryGetValue(typeof(T), out var typedAssets) ||
                    !typedAssets.TryGetValue(fileName, out var asset))
                    return null;

                return new ResourceHandle<T>((T)asset, () => ReleaseCallCount++);
            }

            public UniTask<T> LoadAsync<T>(string fileName, GameObject bindTo,
                CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public UniTask<TResult> LoadScopedAsync<T, TResult>(string fileName,
                Func<T, TResult> use, CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public UniTask<ResourceHandle<T>[]> LoadBatchAsync<T>(IReadOnlyList<string> fileNames,
                CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public bool HasAsset(string fileName) => false;

            public UniTask<GameObject> InstantiateAsync(string fileName, Transform parent = null,
                CancellationToken ct = default) => throw new NotSupportedException();

            public UniTask<T> InstantiateAsync<T>(string fileName, Transform parent = null,
                CancellationToken ct = default) where T : Component => throw new NotSupportedException();

            public UniTask<bool> DownloadByTagAsync(string tag, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public UniTask<bool> DownloadByTagWithRetryAsync(string tag, int maxRetries = 3,
                CancellationToken ct = default) => throw new NotSupportedException();

            public UniTask UnloadUnusedAssetsAsync() => throw new NotSupportedException();

            public UniTask<Scene> LoadSceneAsync(string sceneName,
                LoadSceneMode mode = LoadSceneMode.Single, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public UniTask<bool> UnloadSceneAsync(string sceneName,
                CancellationToken ct = default) => throw new NotSupportedException();
        }

        private readonly List<UnityEngine.Object> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _objects)
            {
                if (obj != null)
                    UnityEngine.Object.DestroyImmediate(obj);
            }
            _objects.Clear();
        }

        [Test]
        public void LoadAsync_SameAddressAndType_LoadsAndReleasesOnce()
        {
            var resources = new TestResourceSystem();
            var asset = CreateAsset<FirstAsset>();
            resources.Add("shared", asset);
            var scope = resources.CreateScope();

            var first = scope.LoadAsync<FirstAsset>("shared").GetAwaiter().GetResult();
            var second = scope.LoadAsync<FirstAsset>("shared").GetAwaiter().GetResult();

            Assert.That(first, Is.SameAs(asset));
            Assert.That(second, Is.SameAs(asset));
            Assert.That(resources.LoadCallCount, Is.EqualTo(1));

            scope.Dispose();
            scope.Dispose();
            Assert.That(resources.ReleaseCallCount, Is.EqualTo(1));
        }

        [Test]
        public void GetLoaded_ReturnsPreviouslyLoadedAssetWithoutAnotherLoad()
        {
            var resources = new TestResourceSystem();
            var asset = CreateAsset<FirstAsset>();
            resources.Add("shared", asset);
            using var scope = resources.CreateScope();

            scope.LoadAsync<FirstAsset>("shared").GetAwaiter().GetResult();
            var result = scope.GetLoaded<FirstAsset>("shared");

            Assert.That(result, Is.SameAs(asset));
            Assert.That(resources.LoadCallCount, Is.EqualTo(1));
        }

        [Test]
        public void GetLoaded_MissingAssetThrowsWithoutLoading()
        {
            var resources = new TestResourceSystem();
            using var scope = resources.CreateScope();

            Assert.Throws<ResourceException>(() =>
                scope.GetLoaded<FirstAsset>("missing"));
            Assert.That(resources.LoadCallCount, Is.Zero);
        }

        [Test]
        public void GetLoaded_DisposedScopeRejectsUse()
        {
            var scope = new TestResourceSystem().CreateScope();
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                scope.GetLoaded<FirstAsset>("asset"));
        }

        [Test]
        public void DifferentScopes_OwnIndependentHandles()
        {
            var resources = new TestResourceSystem();
            resources.Add("shared", CreateAsset<FirstAsset>());
            var firstScope = resources.CreateScope();
            var secondScope = resources.CreateScope();

            firstScope.LoadAsync<FirstAsset>("shared").GetAwaiter().GetResult();
            secondScope.LoadAsync<FirstAsset>("shared").GetAwaiter().GetResult();

            Assert.That(resources.LoadCallCount, Is.EqualTo(2));
            firstScope.Dispose();
            Assert.That(resources.ReleaseCallCount, Is.EqualTo(1));
            secondScope.Dispose();
            Assert.That(resources.ReleaseCallCount, Is.EqualTo(2));
        }

        [Test]
        public void LoadAsync_SameAddressDifferentTypes_OwnsSeparateHandles()
        {
            var resources = new TestResourceSystem();
            resources.Add("shared", CreateAsset<FirstAsset>());
            resources.Add("shared", CreateAsset<SecondAsset>());
            using var scope = resources.CreateScope();

            scope.LoadAsync<FirstAsset>("shared").GetAwaiter().GetResult();
            scope.LoadAsync<SecondAsset>("shared").GetAwaiter().GetResult();

            Assert.That(resources.LoadCallCount, Is.EqualTo(2));
        }

        [Test]
        public void LoadBatchAsync_PreservesOrderAndDeduplicates()
        {
            var resources = new TestResourceSystem();
            var first = CreateAsset<FirstAsset>();
            var second = CreateAsset<FirstAsset>();
            resources.Add("first", first);
            resources.Add("second", second);
            using var scope = resources.CreateScope();

            var result = scope.LoadBatchAsync<FirstAsset>(
                new[] { "second", "first", "second" }).GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo(new[] { second, first, second }));
            Assert.That(resources.LoadCallCount, Is.EqualTo(2));
        }

        [Test]
        public void LoadBatchAsync_EmptyInputIgnoresCancellation()
        {
            var resources = new TestResourceSystem();
            using var scope = resources.CreateScope();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = scope.LoadBatchAsync<FirstAsset>(Array.Empty<string>(), cancellation.Token)
                .GetAwaiter().GetResult();

            Assert.That(result, Is.Empty);
            Assert.That(resources.LoadCallCount, Is.Zero);
        }

        [Test]
        public void MissingResource_ThrowsAndDoesNotLeaveCachedEntry()
        {
            var resources = new TestResourceSystem();
            using var scope = resources.CreateScope();

            Assert.Throws<ResourceException>(() =>
                scope.LoadAsync<FirstAsset>("missing").GetAwaiter().GetResult());
        }

        [Test]
        public void InstantiateAsync_ReusesScopedPrefabHandle()
        {
            var resources = new TestResourceSystem();
            var prefab = CreateGameObject("Prefab", typeof(TestComponent));
            var parent = CreateGameObject("Parent");
            resources.Add("prefab", prefab);
            using var scope = resources.CreateScope();

            var first = scope.InstantiateAsync<TestComponent>("prefab", parent.transform)
                .GetAwaiter().GetResult();
            var second = scope.InstantiateAsync("prefab", parent.transform)
                .GetAwaiter().GetResult();
            _objects.Add(first.gameObject);
            _objects.Add(second);

            Assert.That(first.transform.parent, Is.SameAs(parent.transform));
            Assert.That(second.transform.parent, Is.SameAs(parent.transform));
            Assert.That(resources.LoadCallCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposedScope_RejectsFurtherUse()
        {
            var resources = new TestResourceSystem();
            var scope = resources.CreateScope();
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                scope.LoadAsync<FirstAsset>("asset").GetAwaiter().GetResult());
        }

        [Test]
        public void LoadAsync_CachedAssetIgnoresCancellation()
        {
            var resources = new TestResourceSystem();
            var asset = CreateAsset<FirstAsset>();
            resources.Add("shared", asset);
            using var scope = resources.CreateScope();
            scope.LoadAsync<FirstAsset>("shared").GetAwaiter().GetResult();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = scope.LoadAsync<FirstAsset>("shared", cancellation.Token)
                .GetAwaiter().GetResult();

            Assert.That(result, Is.SameAs(asset));
            Assert.That(resources.LoadCallCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ConcurrentLoad_JoinsOneOperation()
            => Run(async () =>
            {
                var resources = new TestResourceSystem();
                var asset = CreateAsset<FirstAsset>();
                resources.Add("shared", asset);
                resources.BeginDelayingLoads();
                using var scope = resources.CreateScope();

                var first = scope.LoadAsync<FirstAsset>("shared");
                var second = scope.LoadAsync<FirstAsset>("shared");
                Assert.That(resources.LoadCallCount, Is.EqualTo(1));

                resources.CompleteLoads();
                Assert.That(await first, Is.SameAs(asset));
                Assert.That(await second, Is.SameAs(asset));
            });

        [UnityTest]
        public IEnumerator CallerCancellation_StopsWaitingButKeepsSharedLoad()
            => Run(async () =>
            {
                var resources = new TestResourceSystem();
                var asset = CreateAsset<FirstAsset>();
                resources.Add("shared", asset);
                resources.BeginDelayingLoads();
                using var scope = resources.CreateScope();
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                var loading = scope.LoadAsync<FirstAsset>("shared", cancellation.Token);
                Assert.That(resources.LoadCallCount, Is.EqualTo(1));

                var canceled = false;
                try
                {
                    await loading;
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                Assert.That(canceled, Is.True);
                resources.CompleteLoads();
                await UniTask.Yield();

                var result = await scope.LoadAsync<FirstAsset>("shared");
                Assert.That(result, Is.SameAs(asset));
                Assert.That(resources.LoadCallCount, Is.EqualTo(1));
            });

        [UnityTest]
        public IEnumerator DisposeDuringLoad_CancelsWaitAndReleasesLateHandle()
            => Run(async () =>
            {
                var resources = new TestResourceSystem();
                resources.Add("shared", CreateAsset<FirstAsset>());
                resources.BeginDelayingLoads();
                var scope = resources.CreateScope();
                var loading = scope.LoadAsync<FirstAsset>("shared");

                scope.Dispose();
                resources.CompleteLoads();

                var canceled = false;
                try
                {
                    await loading;
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                await UniTask.Yield();
                Assert.That(canceled, Is.True);
                Assert.That(resources.ReleaseCallCount, Is.EqualTo(1));
            });

        private T CreateAsset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            _objects.Add(asset);
            return asset;
        }

        private GameObject CreateGameObject(string name, params Type[] components)
        {
            var gameObject = components.Length == 0
                ? new GameObject(name)
                : new GameObject(name, components);
            _objects.Add(gameObject);
            return gameObject;
        }

        private static IEnumerator Run(Func<UniTask> test)
        {
            return test().ToCoroutine();
        }
    }
}
