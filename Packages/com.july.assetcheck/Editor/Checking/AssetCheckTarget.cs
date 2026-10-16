using System;
using System.IO;
using UnityEditor;

namespace July.AssetCheck
{
    // 资源 GUID 在移动后保持不变；项目设置没有 GUID，使用稳定的项目相对路径。
    internal static class AssetCheckTarget
    {
        internal static readonly string[] ProjectFiles =
        {
            "ProjectSettings/ProjectSettings.asset", "ProjectSettings/QualitySettings.asset",
            "ProjectSettings/GraphicsSettings.asset"
        };

        internal static bool IsProjectFile(string id) => id.StartsWith("ProjectSettings/", StringComparison.Ordinal);
        internal static string Id(string path) => IsProjectFile(path) ? path : AssetDatabase.AssetPathToGUID(path);
        internal static string Resolve(string id) => IsProjectFile(id) ? id : AssetDatabase.GUIDToAssetPath(id);
        internal static string RequirePath(string id)
        {
            var path = Resolve(id);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new IOException("检查对象已移除，请重新检查：" + id);
            return path;
        }
    }
}
