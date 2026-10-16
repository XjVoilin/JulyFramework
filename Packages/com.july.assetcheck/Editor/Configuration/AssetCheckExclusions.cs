using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace July.AssetCheck
{
    // 每次操作重新解析文件夹 GUID，使忽略记录随目录移动和改名保持有效。
    internal sealed class AssetCheckExclusions
    {
        private readonly HashSet<string> _ids;
        private readonly string[] _folders;

        internal AssetCheckExclusions(AssetCheckSettings settings)
        {
            _ids = new HashSet<string>(settings.ignoredAssets.Select(asset => asset.Id), StringComparer.OrdinalIgnoreCase);
            _folders = _ids.Select(AssetCheckTarget.Resolve).Where(AssetDatabase.IsValidFolder)
                .OrderByDescending(path => path.Length).ToArray();
        }

        internal bool ContainsDirectly(string id) => _ids.Contains(id);
        internal bool Contains(string id, string path) => ContainsDirectly(id) || FindParentFolder(path) != null;
        internal string FindParentFolder(string path) => _folders.FirstOrDefault(folder =>
            path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));

        internal static string Key(AssetCheckSettings settings) => string.Join(",", settings.ignoredAssets
            .Select(asset => asset.Id.ToLowerInvariant()).OrderBy(id => id, StringComparer.Ordinal));
    }
}
