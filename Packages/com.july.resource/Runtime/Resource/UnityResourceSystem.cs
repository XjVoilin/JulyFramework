using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace July.Resource
{
    /// <summary>
    /// 基于 Unity Resources 的资源系统实现，用于编辑器测试和无需热更的场景。
    /// 资源必须放在 Resources 文件夹下。
    /// </summary>
    public class UnityResourceSystem : SystemBase, IResourceSystem
    {
        private const string Name = nameof(UnityResourceSystem);

        #region Asset Loading

        public async UniTask<ResourceHandle<T>> LoadAssetAsync<T>(string fileName, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(fileName))
            {
                JLogger.LogWarning($"[{Name}] 资源路径不能为空");
                return null;
            }

            var path = NormalizePath(fileName);

            try
            {
                var request = Resources.LoadAsync<T>(path);
                await request.ToUniTask(cancellationToken: ct);

                if (request.asset == null)
                {
                    JLogger.LogWarning($"[{Name}] 资源加载失败: {path}");
                    return null;
                }

                var resource = request.asset as T;
                if (resource == null)
                {
                    JLogger.LogWarning($"[{Name}] 资源类型不匹配: {path}");
                    return null;
                }

                return new ResourceHandle<T>(resource, () =>
                {
                    if (!(resource is GameObject))
                        Resources.UnloadAsset(resource);
                });
            }
            catch (OperationCanceledException)
            {
                JLogger.LogWarning($"[{Name}] 资源加载已取消: {path}");
                return null;
            }
            catch (Exception ex)
            {
                JLogger.LogException(ex);
                return null;
            }
        }

        public async UniTask<T> LoadAsync<T>(string fileName, GameObject bindTo, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            var handle = await LoadAssetAsync<T>(fileName, ct);
            if (handle == null || !handle.IsValid)
                return null;

            if (bindTo != null)
                handle.BindTo(bindTo);

            return handle.Asset;
        }

        public async UniTask<TResult> LoadScopedAsync<T, TResult>(string fileName, Func<T, TResult> use,
            CancellationToken ct = default) where T : UnityEngine.Object
        {
            using var handle = await LoadAssetAsync<T>(fileName, ct);
            if (handle == null || !handle.IsValid)
                return default;
            return use(handle.Asset);
        }

        public async UniTask<ResourceHandle<T>[]> LoadBatchAsync<T>(IReadOnlyList<string> fileNames,
            CancellationToken ct = default) where T : UnityEngine.Object
        {
            if (fileNames == null || fileNames.Count == 0)
                return Array.Empty<ResourceHandle<T>>();

            var handles = new ResourceHandle<T>[fileNames.Count];
            try
            {
                var tasks = new UniTask<ResourceHandle<T>>[fileNames.Count];
                for (int i = 0; i < fileNames.Count; i++)
                    tasks[i] = LoadAssetAsync<T>(fileNames[i], ct);

                var results = await UniTask.WhenAll(tasks);
                for (int i = 0; i < results.Length; i++)
                    handles[i] = results[i];

                return handles;
            }
            catch
            {
                for (int i = 0; i < handles.Length; i++)
                    handles[i]?.Dispose();
                throw;
            }
        }

        public bool HasAsset(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;

            var path = NormalizePath(fileName);
            var resource = Resources.Load(path);
            if (resource != null)
            {
                if (!(resource is GameObject))
                    Resources.UnloadAsset(resource);
                return true;
            }

            return false;
        }

        #endregion

        #region Instantiate

        public async UniTask<GameObject> InstantiateAsync(string fileName, Transform parent = null,
            CancellationToken ct = default)
        {
            var handle = await LoadAssetAsync<GameObject>(fileName, ct);
            if (handle == null || !handle.IsValid)
                return null;

            var instance = UnityEngine.Object.Instantiate(handle.Asset, parent);
            handle.BindTo(instance);
            return instance;
        }

        public async UniTask<T> InstantiateAsync<T>(string fileName, Transform parent = null,
            CancellationToken ct = default) where T : Component
        {
            var instance = await InstantiateAsync(fileName, parent, ct);
            if (instance == null)
                return null;

            var component = instance.GetComponent<T>();
            if (component == null)
            {
                JLogger.LogWarning($"[{Name}] Prefab '{fileName}' 上未找到组件 {typeof(T).Name}，销毁实例");
                UnityEngine.Object.Destroy(instance);
            }

            return component;
        }

        #endregion

        #region Download

        public UniTask<bool> DownloadByTagAsync(string tag, CancellationToken ct = default)
        {
            // Resources 模式无需下载，直接返回成功
            return UniTask.FromResult(true);
        }

        public UniTask<bool> DownloadByTagWithRetryAsync(string tag, int maxRetries = 3,
            CancellationToken ct = default)
        {
            return UniTask.FromResult(true);
        }

        #endregion

        #region Unload

        public async UniTask UnloadUnusedAssetsAsync()
        {
            await Resources.UnloadUnusedAssets().ToUniTask();
        }

        #endregion

        #region Scene

        public async UniTask<Scene> LoadSceneAsync(
            string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(sceneName))
                throw new ArgumentException("场景名称不能为空", nameof(sceneName));

            var existingScene = SceneManager.GetSceneByName(sceneName);
            if (existingScene.IsValid() && existingScene.isLoaded)
            {
                JLogger.LogWarning($"[{Name}] 场景 {sceneName} 已加载，直接返回");
                return existingScene;
            }

            var asyncOperation = SceneManager.LoadSceneAsync(sceneName, mode);
            if (asyncOperation == null)
                throw new ResourceException($"[{Name}] 场景 {sceneName} 加载失败（场景不存在或路径错误）");

            asyncOperation.allowSceneActivation = true;
            await asyncOperation.ToUniTask(cancellationToken: ct);

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid())
                throw new ResourceException($"[{Name}] 场景 {sceneName} 加载后无效");

            return scene;
        }

        public async UniTask<bool> UnloadSceneAsync(string sceneName, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(sceneName))
                throw new ArgumentException("场景名称不能为空", nameof(sceneName));

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                JLogger.LogWarning($"[{Name}] 场景 {sceneName} 未加载，无需卸载");
                return false;
            }

            var asyncOperation = SceneManager.UnloadSceneAsync(scene);
            if (asyncOperation == null)
            {
                JLogger.LogWarning($"[{Name}] 场景 {sceneName} 卸载失败");
                return false;
            }

            await asyncOperation.ToUniTask(cancellationToken: ct);
            return true;
        }

        #endregion

        #region Private Methods

        private string NormalizePath(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var path = System.IO.Path.ChangeExtension(input, null);

            const string resourcesPrefix = "Resources/";
            if (path.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(resourcesPrefix.Length);

            path = path.Replace('\\', '/');
            return path;
        }

        #endregion
    }
}
