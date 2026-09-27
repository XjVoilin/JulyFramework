#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Resource;
using July.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace July.Guide.Validation
{
    /// <summary>Test-only resource boundary; resolves the repository's actual assets without a bundle build.</summary>
    internal sealed class GuideWindowProvider : IUIWindowProvider
    {
        internal UIAnimationType CloseAnimation;
        public bool TryResolve(int id, out UIOpenOptions options)
        {
            options = new UIOpenOptions
            {
                WindowIdentifier = new WindowIdentifier(900, "TestGuideWindow"),
                Layer = UILayer.Guide, IgnoreSafeArea = true, CloseAnimationType = CloseAnimation
            };
            return id == 900;
        }
    }

    internal sealed class GuideWindowResources : SystemBase, IResourceSystem
    {
        internal const string PrefabPath = "Packages/com.july.guide/Prefabs/DefaultGuideWindow.prefab";
        internal GameObject Template;
        internal int DelayFrames;
        internal int ActiveHandles { get; private set; }

        public async UniTask<ResourceHandle<T>> LoadAssetAsync<T>(string fileName, CancellationToken ct = default)
            where T : Object
        {
            for (var i=0; i<DelayFrames; i++) await UniTask.NextFrame(cancellationToken: ct);
            ct.ThrowIfCancellationRequested();
            if (fileName != "TestGuideWindow") throw new FileNotFoundException(fileName);
            var asset = (Template != null ? Template : AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)) as T;
            if (asset == null) throw new FileNotFoundException(PrefabPath);
            ActiveHandles++;
            return new ResourceHandle<T>(asset, () => ActiveHandles--);
        }

        public async UniTask<T> LoadAsync<T>(string fileName, GameObject bindTo, CancellationToken ct = default)
            where T : Object
        {
            var handle = await LoadAssetAsync<T>(fileName, ct);
            handle.BindTo(bindTo);
            return handle.Asset;
        }

        public async UniTask<TResult> LoadScopedAsync<T, TResult>(string fileName, Func<T, TResult> use,
            CancellationToken ct = default) where T : Object
        {
            using var handle = await LoadAssetAsync<T>(fileName, ct);
            return use(handle.Asset);
        }

        public async UniTask<ResourceHandle<T>[]> LoadBatchAsync<T>(IReadOnlyList<string> fileNames,
            CancellationToken ct = default) where T : Object
        {
            var result = new ResourceHandle<T>[fileNames.Count];
            try
            {
                for (var i = 0; i < result.Length; i++)
                    result[i] = await LoadAssetAsync<T>(fileNames[i], ct);
                return result;
            }
            catch
            {
                foreach (var handle in result) handle?.Dispose();
                throw;
            }
        }

        public bool HasAsset(string fileName) => fileName == "TestGuideWindow";

        public async UniTask<GameObject> InstantiateAsync(string fileName, Transform parent = null,
            CancellationToken ct = default)
        {
            var handle = await LoadAssetAsync<GameObject>(fileName, ct);
            try
            {
                var instance = Object.Instantiate(handle.Asset, parent, false);
                instance.SetActive(true);
                handle.BindTo(instance);
                return instance;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public async UniTask<T> InstantiateAsync<T>(string fileName, Transform parent = null,
            CancellationToken ct = default) where T : Component
        {
            var instance = await InstantiateAsync(fileName, parent, ct);
            return instance.GetComponent<T>();
        }

        // Window tests do not invoke scene or download services.
        public UniTask<Scene> LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken ct = default) => throw new NotSupportedException();
        public UniTask<bool> UnloadSceneAsync(string sceneName, CancellationToken ct = default)
            => throw new NotSupportedException();
        public UniTask<bool> DownloadByTagAsync(string tag, CancellationToken ct = default)
            => throw new NotSupportedException();
        public UniTask<bool> DownloadByTagWithRetryAsync(string tag, int maxRetries = 3,
            CancellationToken ct = default) => throw new NotSupportedException();
        public UniTask UnloadUnusedAssetsAsync() => UniTask.CompletedTask;
    }
}
#endif
