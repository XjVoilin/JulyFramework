using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Resource;
using July.Release;
using July.Launch;
using UnityEngine;
#if !UNITY_EDITOR
using HybridCLR;
#endif

namespace July.Bootstrap
{
    internal static class BootstrapAssemblyLoader
    {
        internal static List<string> Normalize(IEnumerable<string> names)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var asset = ToAssetName(name);
                if (seen.Add(asset)) result.Add(asset);
            }
            return result;
        }

        private static string ToAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("Assembly names must be non-empty filenames.", nameof(name));
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
        }

        internal static async UniTask ReadAndApplyBatchAsync<T>(IReadOnlyList<string> assets,
            Func<string, CancellationToken, UniTask<T>> read, Action<T, int> apply, CancellationToken ct)
            where T : class, IDisposable
        {
            ct.ThrowIfCancellationRequested();
            var handles = new T[assets.Count];
            var reads = new Func<CancellationToken, UniTask<bool>>[assets.Count];
            for (var i = 0; i < assets.Count; i++)
            {
                var index = i;
                reads[i] = async token =>
                {
                    handles[index] = await read(assets[index], token);
                    return true;
                };
            }
            try
            {
                await ParallelLaunchStep.RunAsync(ct, reads);
                for (var i = 0; i < handles.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    apply(handles[i], i);
                }
            }
            finally
            {
                foreach (var handle in handles) handle?.Dispose();
            }
        }

        internal static UniTask LoadAsync(IResourceSystem resource, IReadOnlyList<string> aotAssets, string registrarAssembly, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
#if UNITY_EDITOR
            July.Logging.JLogger.Log("[HybridCLR] Editor: dynamic assembly loading skipped.");
            return UniTask.CompletedTask;
#else
            return LoadPlayerAsync(resource, aotAssets, registrarAssembly, ct);
#endif
        }

#if !UNITY_EDITOR
        private static async UniTask LoadPlayerAsync(IResourceSystem resource, IReadOnlyList<string> aotAssets, string registrarAssembly, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var hotAssets = await ReadHotUpdateAssetsAsync(resource, registrarAssembly, ct);
            // 在首次执行不可逆的程序集装载前，检查声明的资源是否齐全。
            foreach (var asset in aotAssets) RequireAsset(resource, asset);
            foreach (var asset in hotAssets) RequireAsset(resource, asset);
            await ReadAndApplyBatchAsync(aotAssets,
                (asset, token) => resource.LoadAssetAsync<TextAsset>(asset, token),
                (handle, index) =>
                {
                    var asset = aotAssets[index];
                    if (handle?.Asset == null) throw new InvalidOperationException($"Empty AOT asset: {asset}");
                    var result = RuntimeApi.LoadMetadataForAOTAssembly(handle.Asset.bytes, HomologousImageMode.SuperSet);
                    if (result != LoadImageErrorCode.OK)
                        throw new InvalidOperationException($"AOT metadata {asset}: {result}. Restart after correcting the release.");
                }, ct);
            foreach (var asset in hotAssets)
            {
                ct.ThrowIfCancellationRequested();
                using var handle = await resource.LoadAssetAsync<TextAsset>(asset, ct);
                ct.ThrowIfCancellationRequested();
                if (handle?.Asset == null) throw new InvalidOperationException($"Empty hot-update asset: {asset}");
                Assembly.Load(handle.Asset.bytes);
            }

        }

        private static async UniTask<List<string>> ReadHotUpdateAssetsAsync(IResourceSystem resource,
            string registrarAssembly, CancellationToken ct)
        {
            RequireAsset(resource, HybridClrManifest.AssetAddress);
            using var handle = await resource.LoadAssetAsync<TextAsset>(HybridClrManifest.AssetAddress, ct);
            ct.ThrowIfCancellationRequested();
            if (handle?.Asset == null)
                throw new InvalidOperationException("热更程序集清单为空，请重新生成并发布资源。");
            // 清单由构建生成，顺序已按 DLL 依赖排列；读取完就释放文本资源。
            return Normalize(HybridClrManifest.ReadHotUpdateAssemblies(handle.Asset.text, registrarAssembly));
        }

        private static void RequireAsset(IResourceSystem resource, string asset)
        {
            if (!resource.HasAsset(asset))
                throw new InvalidOperationException($"启动所需资源不存在：{asset}。请重新生成并发布资源。");
        }
#endif
    }
}
