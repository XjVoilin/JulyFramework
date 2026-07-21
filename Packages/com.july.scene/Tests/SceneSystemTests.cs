using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Resource;
using July.Arch;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace July.Scene.Tests
{
    [TestFixture]
    public sealed class SceneSystemTests
    {
        private sealed class TestResourceSystem : SystemBase, IResourceSystem
        {
            public int UnloadUnusedAssetsCount { get; private set; }

            public UniTask UnloadUnusedAssetsAsync()
            {
                UnloadUnusedAssetsCount++;
                return UniTask.CompletedTask;
            }

            public UniTask<UnityScene> LoadSceneAsync(string sceneName,
                LoadSceneMode mode = LoadSceneMode.Single, CancellationToken ct = default)
                => UniTask.FromResult(SceneManager.GetActiveScene());

            public UniTask<ResourceHandle<T>> LoadAssetAsync<T>(string fileName,
                CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public UniTask<T> LoadAsync<T>(string fileName, GameObject bindTo,
                CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public UniTask<TResult> LoadScopedAsync<T, TResult>(string fileName, Func<T, TResult> use,
                CancellationToken ct = default) where T : UnityEngine.Object =>
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

            public UniTask<bool> UnloadSceneAsync(string sceneName, CancellationToken ct = default) =>
                throw new NotSupportedException();
        }

        private ArchContext _context;
        private TestResourceSystem _resources;
        private SceneSystem _scenes;

        [SetUp]
        public void SetUp()
        {
            _context = new ArchContext();
            _resources = new TestResourceSystem();
            _scenes = new SceneSystem();
            _context.RegisterSystem(_resources);
            _context.RegisterSystem(_scenes);
            _context.InitializeAsync().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _context?.Shutdown();
            _context = null;
            _resources = null;
            _scenes = null;
        }

        [Test]
        public void SwitchScene_Default_CleansUnusedAssets()
        {
            _scenes.SwitchSceneAsync("Lobby").GetAwaiter().GetResult();

            Assert.That(_resources.UnloadUnusedAssetsCount, Is.EqualTo(1));
        }

        [Test]
        public void SwitchScene_Deferred_SkipsCurrentCleanup()
        {
            _scenes.SwitchSceneAsync("Lobby", deferUnusedAssetCleanup: true)
                .GetAwaiter().GetResult();

            Assert.That(_resources.UnloadUnusedAssetsCount, Is.Zero);
        }

        [Test]
        public void SwitchScene_DefaultAfterDeferred_CleansUnusedAssetsOnce()
        {
            _scenes.SwitchSceneAsync("Lobby", deferUnusedAssetCleanup: true)
                .GetAwaiter().GetResult();

            _scenes.SwitchSceneAsync("Game101").GetAwaiter().GetResult();

            Assert.That(_resources.UnloadUnusedAssetsCount, Is.EqualTo(1));
        }
    }
}
