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

        /// <summary>
        /// 开始指定标签的后台预下载；复用运行中的任务，恢复已暂停的任务。
        /// 空集合不执行操作。文件缓存由资源实现持有，不要求调用方保存任务。
        /// </summary>
        void StartPreDownloads(params string[] tags);

        /// <summary>
        /// 暂停当前预下载。排除标签只跳过本次暂停，不恢复其原有暂停状态。
        /// 不阻止后续显式 Start，也不阻断已被普通下载接续的必需准备。
        /// 在途文件是否继续由底层实现决定。
        /// </summary>
        void PausePreDownloads(params string[] exceptTags);

        /// <summary>恢复全部现有预下载，不创建新任务。</summary>
        void ResumePreDownloads();

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
