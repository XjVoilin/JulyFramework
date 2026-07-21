using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace July.Resource
{
    /// <summary>
    /// 资源系统接口 — 统一资源加载、实例化、下载、场景管理。
    /// 通过 Scope.GetSystem&lt;IResourceSystem&gt;() 获取。
    /// </summary>
    public interface IResourceSystem
    {
        #region Asset Loading

        UniTask<ResourceHandle<T>> LoadAssetAsync<T>(string fileName, CancellationToken ct = default)
            where T : UnityEngine.Object;

        UniTask<T> LoadAsync<T>(string fileName, GameObject bindTo, CancellationToken ct = default)
            where T : UnityEngine.Object;

        UniTask<TResult> LoadScopedAsync<T, TResult>(string fileName, Func<T, TResult> use,
            CancellationToken ct = default)
            where T : UnityEngine.Object;

        UniTask<ResourceHandle<T>[]> LoadBatchAsync<T>(IReadOnlyList<string> fileNames,
            CancellationToken ct = default)
            where T : UnityEngine.Object;

        bool HasAsset(string fileName);

        #endregion

        #region Instantiate

        UniTask<GameObject> InstantiateAsync(string fileName, Transform parent = null,
            CancellationToken ct = default);

        UniTask<T> InstantiateAsync<T>(string fileName, Transform parent = null,
            CancellationToken ct = default)
            where T : Component;

        #endregion

        #region Download

        UniTask<bool> DownloadByTagAsync(string tag, CancellationToken ct = default);

        UniTask<bool> DownloadByTagWithRetryAsync(string tag, int maxRetries = 3,
            CancellationToken ct = default);

        #endregion

        #region Unload

        UniTask UnloadUnusedAssetsAsync();

        #endregion

        #region Scene

        UniTask<Scene> LoadSceneAsync(string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken ct = default);

        UniTask<bool> UnloadSceneAsync(string sceneName, CancellationToken ct = default);

        #endregion
    }
}
