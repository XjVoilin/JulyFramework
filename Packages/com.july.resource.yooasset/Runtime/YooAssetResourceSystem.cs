using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Resource;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using Object = UnityEngine.Object;

namespace July.Resource.YooAsset
{
    /// <summary>
    /// July.Resource 的 YooAsset Implementation。
    /// 支持在 Arch 生命周期之前显式初始化；生命周期再次初始化时会复用同一个任务。
    /// </summary>
    public sealed class YooAssetResourceSystem : SystemBase, IResourceSystem
    {
        private readonly YooAssetOptions _options;
        private readonly Dictionary<string, SceneHandle> _sceneHandles = new();
        private UniTask _initializationTask;
        private bool _initializationStarted;

        public ResourcePackage Package { get; private set; }
        public bool IsInitialized => Package != null &&
                                     Package.InitializeStatus == EOperationStatus.Succeed;

        public YooAssetResourceSystem(YooAssetOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 提前初始化资源系统。多次调用共享同一个初始化过程，不会重复创建或初始化 Package。
        /// 调用方取消只停止本次等待，不会中断其他调用方共享的初始化过程。
        /// </summary>
        public UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!_initializationStarted)
            {
                _initializationStarted = true;
                _initializationTask = InitializeCoreAsync().Preserve();
            }

            return cancellationToken.CanBeCanceled
                ? _initializationTask.AttachExternalCancellation(cancellationToken)
                : _initializationTask;
        }

        protected override UniTask OnInitializeAsync() => InitializeAsync();

        private async UniTask InitializeCoreAsync()
        {
            _options.Validate();
            YooAssets.Initialize();
            Package = YooAssets.TryGetPackage(_options.PackageName) ??
                      YooAssets.CreatePackage(_options.PackageName);

            if (_options.SetAsDefaultPackage)
                YooAssets.SetDefaultPackage(Package);

            if (Package.InitializeStatus == EOperationStatus.None)
            {
                var parameters = _options.CreateInitializeParameters?.Invoke(Package) ??
                                 CreateDefaultInitializeParameters();
                if (parameters == null)
                    throw new InvalidOperationException("YooAsset 初始化参数工厂返回了 null。");

                var initialize = Package.InitializeAsync(parameters);
                await UniTask.WaitUntil(() => initialize.IsDone);
                EnsureSucceeded(initialize.Status, initialize.Error, "初始化资源包");
            }
            else if (Package.InitializeStatus == EOperationStatus.Processing)
            {
                await UniTask.WaitUntil(() => Package.InitializeStatus != EOperationStatus.Processing);
            }

            if (Package.InitializeStatus != EOperationStatus.Succeed)
            {
                throw new InvalidOperationException(
                    $"资源包 '{_options.PackageName}' 当前状态为 {Package.InitializeStatus}。");
            }

            if (_options.UpdateManifestAfterInitialization)
                await UpdateManifestAsync();
        }

        private InitializeParameters CreateDefaultInitializeParameters()
        {
            switch (_options.PlayMode)
            {
                case EPlayMode.EditorSimulateMode:
#if UNITY_EDITOR
                    var buildResult = EditorSimulateModeHelper.SimulateBuild(_options.PackageName);
                    return new EditorSimulateModeParameters
                    {
                        EditorFileSystemParameters =
                            FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                                buildResult.PackageRootDirectory)
                    };
#else
                    throw new InvalidOperationException("EditorSimulateMode 只能在 Unity Editor 中使用。");
#endif
                case EPlayMode.OfflinePlayMode:
                    return new OfflinePlayModeParameters
                    {
                        BuildinFileSystemParameters =
                            FileSystemParameters.CreateDefaultBuildinFileSystemParameters()
                    };
                case EPlayMode.HostPlayMode:
                    var remote = new RemoteServices(
                        _options.DefaultHostServer, _options.FallbackHostServer);
                    return new HostPlayModeParameters
                    {
                        BuildinFileSystemParameters =
                            FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
                        CacheFileSystemParameters =
                            FileSystemParameters.CreateDefaultCacheFileSystemParameters(remote)
                    };
                case EPlayMode.WebPlayMode:
                    return new WebPlayModeParameters
                    {
                        WebServerFileSystemParameters =
                            FileSystemParameters.CreateDefaultWebServerFileSystemParameters()
                    };
                default:
                    throw new InvalidOperationException(
                        $"运行模式 {_options.PlayMode} 需要自定义初始化参数工厂。");
            }
        }

        public async UniTask UpdateManifestAsync(CancellationToken cancellationToken = default)
        {
            EnsurePackage();
            var version = Package.RequestPackageVersionAsync(true);
            await UniTask.WaitUntil(() => version.IsDone, cancellationToken: cancellationToken);
            EnsureSucceeded(version.Status, version.Error, "请求资源版本");

            var manifest = Package.UpdatePackageManifestAsync(version.PackageVersion);
            await UniTask.WaitUntil(() => manifest.IsDone, cancellationToken: cancellationToken);
            EnsureSucceeded(manifest.Status, manifest.Error, "更新资源清单");
        }

        public async UniTask<ResourceHandle<T>> LoadAssetAsync<T>(string fileName,
            CancellationToken ct = default) where T : Object
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            EnsurePackage();

            AssetHandle handle = null;
            try
            {
                handle = Package.LoadAssetAsync<T>(fileName);
                await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: ct);
                if (!handle.IsValid || handle.AssetObject is not T asset)
                {
                    handle.Release();
                    return null;
                }

                return new ResourceHandle<T>(asset, handle.Release);
            }
            catch
            {
                handle?.Release();
                throw;
            }
        }

        public async UniTask<T> LoadAsync<T>(string fileName, GameObject bindTo,
            CancellationToken ct = default) where T : Object
        {
            var handle = await LoadAssetAsync<T>(fileName, ct);
            if (handle == null || !handle.IsValid) return null;
            if (bindTo != null) handle.BindTo(bindTo);
            else handle.MarkPermanent();
            return handle.Asset;
        }

        public async UniTask<TResult> LoadScopedAsync<T, TResult>(string fileName,
            Func<T, TResult> use, CancellationToken ct = default) where T : Object
        {
            if (use == null) throw new ArgumentNullException(nameof(use));
            using var handle = await LoadAssetAsync<T>(fileName, ct);
            return handle == null || !handle.IsValid ? default : use(handle.Asset);
        }

        public async UniTask<ResourceHandle<T>[]> LoadBatchAsync<T>(
            IReadOnlyList<string> fileNames, CancellationToken ct = default) where T : Object
        {
            if (fileNames == null || fileNames.Count == 0)
                return Array.Empty<ResourceHandle<T>>();

            var handles = new ResourceHandle<T>[fileNames.Count];
            var tasks = new UniTask[fileNames.Count];
            for (var i = 0; i < fileNames.Count; i++)
                tasks[i] = LoadIntoSlotAsync(fileNames[i], handles, i, ct);

            try
            {
                await UniTask.WhenAll(tasks);
                return handles;
            }
            catch
            {
                foreach (var handle in handles) handle?.Dispose();
                throw;
            }
        }

        private async UniTask LoadIntoSlotAsync<T>(string fileName,
            ResourceHandle<T>[] handles, int index, CancellationToken ct) where T : Object
        {
            handles[index] = await LoadAssetAsync<T>(fileName, ct);
        }

        public bool HasAsset(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || Package == null) return false;
            EnsurePackage();
            return Package.GetAssetInfo(fileName) != null;
        }

        public async UniTask<GameObject> InstantiateAsync(string fileName, Transform parent = null,
            CancellationToken ct = default)
        {
            var handle = await LoadAssetAsync<GameObject>(fileName, ct);
            if (handle == null || !handle.IsValid) return null;
            var instance = Object.Instantiate(handle.Asset, parent);
            handle.BindTo(instance);
            return instance;
        }

        public async UniTask<T> InstantiateAsync<T>(string fileName, Transform parent = null,
            CancellationToken ct = default) where T : Component
        {
            var instance = await InstantiateAsync(fileName, parent, ct);
            if (instance == null) return null;
            var component = instance.GetComponent<T>();
            if (component != null) return component;
            Object.Destroy(instance);
            return null;
        }

        public async UniTask<bool> DownloadByTagAsync(string tag,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tag))
                throw new ArgumentException("Tag 不能为空。", nameof(tag));
            EnsurePackage();
            var downloader = Package.CreateResourceDownloader(tag,
                _options.MaxConcurrentDownloads, _options.DownloadRetryCount);
            if (downloader.TotalDownloadCount == 0) return true;

            downloader.BeginDownload();
            try
            {
                await UniTask.WaitUntil(() => downloader.Status != EOperationStatus.Processing,
                    cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                downloader.CancelDownload();
                throw;
            }
            return downloader.Status == EOperationStatus.Succeed;
        }

        public async UniTask<bool> DownloadByTagWithRetryAsync(string tag, int maxRetries = 3,
            CancellationToken ct = default)
        {
            if (maxRetries <= 0) throw new ArgumentOutOfRangeException(nameof(maxRetries));
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (await DownloadByTagAsync(tag, ct)) return true;
                if (attempt < maxRetries)
                    await UniTask.Delay(attempt * 1000, cancellationToken: ct);
            }
            return false;
        }

        public async UniTask UnloadUnusedAssetsAsync()
        {
            EnsurePackage();
            var operation = Package.UnloadUnusedAssetsAsync();
            await UniTask.WaitUntil(() => operation.IsDone);
        }

        public async UniTask<Scene> LoadSceneAsync(string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                throw new ArgumentException("场景名不能为空。", nameof(sceneName));
            EnsurePackage();

            SceneHandle handle = null;
            try
            {
                handle = Package.LoadSceneAsync(sceneName, mode);
                await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: ct);
                if (!handle.IsValid)
                    throw new InvalidOperationException($"场景加载失败：{sceneName}");
                _sceneHandles[sceneName] = handle;
                return handle.SceneObject;
            }
            catch
            {
                handle?.Release();
                throw;
            }
        }

        public async UniTask<bool> UnloadSceneAsync(string sceneName,
            CancellationToken ct = default)
        {
            if (_sceneHandles.Remove(sceneName, out var handle) && handle != null && handle.IsValid)
            {
                var operation = handle.UnloadAsync();
                await UniTask.WaitUntil(() => operation.IsDone, cancellationToken: ct);
                handle.Release();
                return true;
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded) return false;
            var fallback = SceneManager.UnloadSceneAsync(scene);
            if (fallback == null) return false;
            await UniTask.WaitUntil(() => fallback.isDone, cancellationToken: ct);
            return true;
        }

        protected override void OnShutdown()
        {
            foreach (var handle in _sceneHandles.Values)
                if (handle != null && handle.IsValid) handle.Release();
            _sceneHandles.Clear();
            Package = null;
            _initializationStarted = false;
            _initializationTask = default;
        }

        private void EnsurePackage()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("YooAsset 资源包尚未成功初始化。");
        }

        private static void EnsureSucceeded(EOperationStatus status, string error, string operation)
        {
            if (status != EOperationStatus.Succeed)
                throw new InvalidOperationException($"{operation}失败：{error}");
        }

        private sealed class RemoteServices : IRemoteServices
        {
            private readonly string _main;
            private readonly string _fallback;

            public RemoteServices(string main, string fallback)
            {
                _main = main.TrimEnd('/');
                _fallback = string.IsNullOrWhiteSpace(fallback) ? _main : fallback.TrimEnd('/');
            }

            public string GetRemoteMainURL(string fileName) => $"{_main}/{fileName}";
            public string GetRemoteFallbackURL(string fileName) => $"{_fallback}/{fileName}";
        }
    }
}
