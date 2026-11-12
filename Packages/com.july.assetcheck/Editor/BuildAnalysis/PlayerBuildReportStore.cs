using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace July.AssetCheck
{
    [Serializable]
    internal sealed class PlayerBuildAsset
    {
        public string guid, path, type;
        public ulong bytes;
    }

    [Serializable]
    internal sealed class PlayerBuildSnapshot
    {
        public int version = 1;
        public string platform, builtAt, unityVersion, outputPath;
        public bool development;
        public ulong totalBytes, assetBytes;
        public PlayerBuildAsset[] assets;
    }

    internal static class PlayerBuildReportStore
    {
        internal const string RelativePath = "Library/July/AssetCheck/PlayerBuild/latest.json";
        internal static event Action Changed;

        internal static PlayerBuildSnapshot Capture(BuildReport report)
        {
            var assets = new Dictionary<string, PlayerBuildAsset>(StringComparer.Ordinal);
            var types = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var packed in report.packedAssets)
            foreach (var content in packed.contents)
            {
                var guid = content.sourceAssetGUID.ToString();
                if (guid == "00000000000000000000000000000000") guid = "";
                var path = string.IsNullOrEmpty(content.sourceAssetPath) ? "Unity 内部生成数据" : content.sourceAssetPath;
                var key = guid.Length > 0 ? "guid:" + guid : "path:" + path;
                if (!assets.TryGetValue(key, out var asset))
                {
                    assets.Add(key, asset = new PlayerBuildAsset { guid = guid, path = path });
                    types.Add(key, new SortedSet<string>(StringComparer.Ordinal));
                }
                // 保留同一源资源在不同数据文件中的物理占用，不按 GUID 丢弃副本。
                asset.bytes = checked(asset.bytes + content.packedSize);
                types[key].Add(content.type?.Name ?? "内部数据");
            }
            ulong bytes = 0;
            foreach (var pair in assets)
            {
                pair.Value.type = string.Join(" / ", types[pair.Key]);
                bytes = checked(bytes + pair.Value.bytes);
            }
            var summary = report.summary;
            return new PlayerBuildSnapshot
            {
                platform = summary.platform.ToString(), builtAt = summary.buildEndedAt.ToString("O"),
                unityVersion = Application.unityVersion, outputPath = summary.outputPath,
                development = (summary.options & BuildOptions.Development) != 0,
                totalBytes = summary.totalSize, assetBytes = bytes,
                assets = assets.Values.OrderByDescending(asset => asset.bytes).ThenBy(asset => asset.path, StringComparer.Ordinal).ToArray()
            };
        }

        internal static void Save(PlayerBuildSnapshot snapshot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RelativePath));
            var temporary = RelativePath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(snapshot, true));
            if (File.Exists(RelativePath)) File.Replace(temporary, RelativePath, null);
            else File.Move(temporary, RelativePath);
            Changed?.Invoke();
        }

        internal static PlayerBuildSnapshot Load()
        {
            if (!File.Exists(RelativePath)) return null;
            var snapshot = JsonUtility.FromJson<PlayerBuildSnapshot>(File.ReadAllText(RelativePath));
            if (snapshot == null || snapshot.version != 1 || snapshot.assets == null ||
                snapshot.assets.Any(asset => asset == null || string.IsNullOrEmpty(asset.path)))
                throw new InvalidDataException("构建资源记录无效，请完成一次客户端构建以重新生成：" + RelativePath);
            return snapshot;
        }
    }

    // 在回调中保留资源数据；等构建结束后补齐最终结果和总大小。
    internal sealed class PlayerBuildReportCapture : IPostprocessBuildWithReport
    {
        private sealed class PendingReport
        {
            internal BuildReport Report;
            internal PlayerBuildSnapshot Snapshot;
        }

        private static PendingReport _pending;
        public int callbackOrder => int.MaxValue;

        public void OnPostprocessBuild(BuildReport report)
        {
            // 只分析最后一次 Player 构建，不借用前一次构建的资源明细。
            _pending = new PendingReport { Report = report, Snapshot = PlayerBuildReportStore.Capture(report) };
            EditorApplication.update -= Complete;
            EditorApplication.update += Complete;
        }

        private static void Complete()
        {
            if (BuildPipeline.isBuildingPlayer) return;
            EditorApplication.update -= Complete;
            var pending = _pending;
            _pending = null;
            var summary = pending.Report.summary;
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) return;
            var snapshot = pending.Snapshot;
            snapshot.builtAt = summary.buildEndedAt.ToString("O");
            snapshot.totalBytes = summary.totalSize;
            PlayerBuildReportStore.Save(snapshot);
        }
    }
}
