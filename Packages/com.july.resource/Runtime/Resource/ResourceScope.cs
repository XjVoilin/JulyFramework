using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.Resource
{
    /// <summary>
    /// 管理一组具有相同上层生命周期的资源租约。
    /// 同一 Scope 内，相同地址和资源类型只会持有一个底层句柄。
    /// Scope 不是线程安全的，必须在 Unity 主线程创建、使用和释放。
    /// </summary>
    public sealed class ResourceScope : IDisposable
    {
        private readonly IResourceSystem _resourceSystem;
        private readonly Dictionary<ResourceKey, Object> _assets = new();
        private readonly Dictionary<ResourceKey, UniTaskCompletionSource<Object>> _loading = new();
        private readonly List<IDisposable> _handles = new();
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private readonly CancellationToken _lifetimeToken;
        private bool _disposed;

        internal ResourceScope(IResourceSystem resourceSystem)
        {
            _resourceSystem = resourceSystem;
            _lifetimeToken = _lifetimeCancellation.Token;
        }

        /// <summary>
        /// 加载并持有资源。重复或并发加载相同地址和类型时复用同一个底层句柄。
        /// 调用方取消只停止本次等待，不会取消 Scope 内共享的加载。
        /// </summary>
        public UniTask<T> LoadAsync<T>(string fileName, CancellationToken ct = default)
            where T : Object
        {
            ValidateFileName(fileName);
            ThrowIfDisposed();

            var key = new ResourceKey(fileName, typeof(T));
            if (_assets.TryGetValue(key, out var asset))
                return UniTask.FromResult((T)asset);

            if (!_loading.TryGetValue(key, out var completion))
            {
                completion = new UniTaskCompletionSource<Object>();
                _loading.Add(key, completion);
                LoadAndStoreAsync<T>(key, completion).Forget();
            }

            return AwaitAssetAsync<T>(completion.Task, ct);
        }

        /// <summary>
        /// 获取当前 Scope 已加载并持有的资源，不触发新的加载。
        /// </summary>
        public T GetLoaded<T>(string fileName) where T : Object
        {
            ValidateFileName(fileName);
            ThrowIfDisposed();

            var key = new ResourceKey(fileName, typeof(T));
            if (!_assets.TryGetValue(key, out var asset))
            {
                throw new ResourceException(
                    $"资源尚未加载：'{fileName}' ({typeof(T).Name})。");
            }

            return (T)asset;
        }

        /// <summary>
        /// 并发加载一批资源，返回结果顺序与输入顺序一致。
        /// 已成功加载的资源由 Scope 持有；该操作不提供事务回滚语义。
        /// </summary>
        public async UniTask<T[]> LoadBatchAsync<T>(IReadOnlyList<string> fileNames,
            CancellationToken ct = default) where T : Object
        {
            if (fileNames == null) throw new ArgumentNullException(nameof(fileNames));
            ThrowIfDisposed();
            if (fileNames.Count == 0) return Array.Empty<T>();

            var tasks = new UniTask<T>[fileNames.Count];
            for (var index = 0; index < fileNames.Count; index++)
                tasks[index] = LoadAsync<T>(fileNames[index], ct);

            return await UniTask.WhenAll(tasks);
        }

        /// <summary>
        /// 加载并实例化 Prefab。Scope 持有 Prefab 资源，调用方持有实例；
        /// 调用方必须在释放 Scope 前销毁实例。
        /// </summary>
        public async UniTask<GameObject> InstantiateAsync(string fileName, Transform parent = null,
            CancellationToken ct = default)
        {
            var prefab = await LoadAsync<GameObject>(fileName, ct);
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            return Object.Instantiate(prefab, parent);
        }

        /// <summary>
        /// 加载并实例化 Prefab，然后返回指定组件。Prefab 缺少组件时销毁实例并抛出异常。
        /// </summary>
        public async UniTask<T> InstantiateAsync<T>(string fileName, Transform parent = null,
            CancellationToken ct = default) where T : Component
        {
            var instance = await InstantiateAsync(fileName, parent, ct);
            var component = instance.GetComponent<T>();
            if (component != null) return component;

            Object.Destroy(instance);
            throw new ResourceException(
                $"Prefab '{fileName}' 上未找到组件 {typeof(T).Name}。");
        }

        /// <summary>
        /// 取消尚未完成的共享加载并释放当前 Scope 持有的全部资源句柄。
        /// 重复调用不会重复释放资源。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _lifetimeCancellation.Cancel();
            _loading.Clear();

            foreach (var handle in _handles)
                handle.Dispose();

            _assets.Clear();
            _handles.Clear();
            _lifetimeCancellation.Dispose();
        }

        private async UniTask LoadAndStoreAsync<T>(ResourceKey key,
            UniTaskCompletionSource<Object> completion) where T : Object
        {
            ResourceHandle<T> handle = null;
            try
            {
                handle = await _resourceSystem.LoadAssetAsync<T>(
                    key.FileName, _lifetimeToken);

                if (_disposed)
                {
                    completion.TrySetCanceled(_lifetimeToken);
                    return;
                }

                if (handle == null || !handle.IsValid)
                {
                    throw new ResourceException(
                        $"资源加载失败：'{key.FileName}' ({typeof(T).Name})。");
                }

                var asset = handle.Asset;
                _assets.Add(key, asset);
                _handles.Add(handle);
                handle = null;
                completion.TrySetResult(asset);
            }
            catch (OperationCanceledException)
                when (_lifetimeCancellation.IsCancellationRequested)
            {
                completion.TrySetCanceled(_lifetimeToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                handle?.Dispose();
                _loading.Remove(key);
            }
        }

        private static async UniTask<T> AwaitAssetAsync<T>(UniTask<Object> loading,
            CancellationToken ct) where T : Object
        {
            var asset = ct.CanBeCanceled
                ? await loading.AttachExternalCancellation(ct)
                : await loading;
            return (T)asset;
        }

        private static void ValidateFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("资源地址不能为空。", nameof(fileName));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResourceScope));
        }

        private readonly struct ResourceKey : IEquatable<ResourceKey>
        {
            internal readonly string FileName;
            private readonly Type _assetType;

            internal ResourceKey(string fileName, Type assetType)
            {
                FileName = fileName;
                _assetType = assetType;
            }

            public bool Equals(ResourceKey other)
            {
                return _assetType == other._assetType &&
                       string.Equals(FileName, other.FileName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ResourceKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (StringComparer.Ordinal.GetHashCode(FileName) * 397) ^
                           _assetType.GetHashCode();
                }
            }
        }
    }

    public static class ResourceScopeExtensions
    {
        /// <summary>
        /// 创建一个由调用方定义生命周期的资源 Scope。
        /// </summary>
        public static ResourceScope CreateScope(this IResourceSystem resourceSystem)
        {
            if (resourceSystem == null) throw new ArgumentNullException(nameof(resourceSystem));
            return new ResourceScope(resourceSystem);
        }
    }
}
