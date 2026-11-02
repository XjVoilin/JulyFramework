using System;
using System.Linq;

namespace July.AssetCheck
{
    // A display scope over recorded paths; no AssetDatabase lookup and no effect on inspection inputs.
    internal static class BundleResourceDirectoryFilter
    {
        internal static string Normalize(string directory)
        {
            var path = (directory ?? "").Trim().Replace('\\', '/');
            if (path.Length == 0) return "";
            path = path.TrimEnd('/');
            if (!(path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal) ||
                  path == "Packages" || path.StartsWith("Packages/", StringComparison.Ordinal)) ||
                path.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."))
                throw new ArgumentException("请输入 Assets 或 Packages 下的项目相对目录；可填写历史目录，无需当前存在。");
            return path;
        }

        internal static bool Contains(string directory, string recordedPath) => directory.Length == 0 ||
            recordedPath.StartsWith(directory + "/", StringComparison.Ordinal);
    }
}
