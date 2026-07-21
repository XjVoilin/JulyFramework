using System;
using July.Resource;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class TipManagerTests
    {
        private sealed class ImmediateResourceSystem : IResourceSystem
        {
            private readonly GameObject _prefab;

            public ImmediateResourceSystem(GameObject prefab)
            {
                _prefab = prefab;
            }

            public UniTask<ResourceHandle<T>> LoadAssetAsync<T>(string fileName,
                CancellationToken ct = default) where T : UnityEngine.Object
            {
                var asset = (T)(UnityEngine.Object)_prefab;
                return UniTask.FromResult(new ResourceHandle<T>(asset, () => { }));
            }

            public UniTask<T> LoadAsync<T>(string fileName, GameObject bindTo,
                CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public UniTask<TResult> LoadScopedAsync<T, TResult>(string fileName, Func<T, TResult> use,
                CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public UniTask<ResourceHandle<T>[]> LoadBatchAsync<T>(IReadOnlyList<string> fileNames,
                CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public bool HasAsset(string fileName) => true;

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

            public UniTask<bool> UnloadSceneAsync(string sceneName, CancellationToken ct = default) =>
                throw new NotSupportedException();
        }

        [UnityTest]
        public IEnumerator FirstShow_DisplaysMessageAfterLazyPrefabLoad()
        {
            var prefab = new GameObject("TipPrefab", typeof(RectTransform), typeof(CanvasGroup),
                typeof(UITipItem));
            var resources = new ImmediateResourceSystem(prefab);
            var manager = new TipManager(() => resources);

            manager.Initialize();
            manager.Show("first message", 10f);
            yield return null;

            var isVisible = UnityEngine.Object.FindObjectsOfType<UITipItem>(true)
                .Any(item => item.gameObject.activeSelf && item.Message == "first message");

            manager.Shutdown();
            UnityEngine.Object.Destroy(prefab);
            yield return null;

            Assert.That(isVisible, Is.True,
                "The first tip should display after its lazily loaded prefab becomes available.");
        }
    }
}
