using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    // 每次操作重新解析文件夹 GUID，使忽略记录随目录移动和改名保持有效。
    internal sealed class AssetCheckExclusions
    {
        private readonly HashSet<string> _ids;
        private readonly string[] _folders;
        private readonly string[] _historicalFolders;
        internal int UnresolvedHistoricalRecords { get; }

        internal AssetCheckExclusions(AssetCheckSettings settings)
        {
            _ids = new HashSet<string>(settings.ignoredAssets.Select(asset => asset.Id), StringComparer.OrdinalIgnoreCase);
            _folders = _ids.Select(id => new { Id = id, Path = AssetCheckTarget.Resolve(id) })
                .Where(entry => AssetDatabase.IsValidFolder(entry.Path) && string.Equals(entry.Id,
                    AssetDatabase.AssetPathToGUID(entry.Path, AssetPathToGUIDOptions.OnlyExistingAssets), StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Path).OrderByDescending(path => path.Length).ToArray();
            _historicalFolders = settings.ignoredAssets.Where(asset => asset.targetType == IgnoredTargetType.Folder)
                .SelectMany(asset => asset.folderPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            UnresolvedHistoricalRecords = settings.ignoredAssets.Count(asset => asset.targetType == IgnoredTargetType.Unknown &&
                string.IsNullOrEmpty(asset.projectPath) && !string.Equals(asset.Id, AssetDatabase.AssetPathToGUID(
                    AssetCheckTarget.Resolve(asset.Id), AssetPathToGUIDOptions.OnlyExistingAssets), StringComparison.OrdinalIgnoreCase));
        }

        internal bool ContainsDirectly(string id) => _ids.Contains(id);
        internal bool Contains(string id, string path) => ContainsDirectly(id) || FindParentFolder(path) != null;
        internal bool ContainsHistorical(string id, string reportPath) => Contains(id, reportPath) || _historicalFolders.Any(folder =>
            reportPath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
        internal string FindParentFolder(string path) => _folders.FirstOrDefault(folder =>
            path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));

        internal static string Key(AssetCheckSettings settings) => string.Join(",", settings.ignoredAssets
            .Select(asset => asset.Id.ToLowerInvariant()).OrderBy(id => id, StringComparer.Ordinal));

        internal static string HistoricalKey(AssetCheckSettings settings) => string.Join(",", settings.ignoredAssets
            .OrderBy(asset => asset.Id, StringComparer.OrdinalIgnoreCase).Select(asset => JsonUtility.ToJson(asset)));
    }
}
