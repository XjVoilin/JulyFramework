using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    internal static class AssetCheckProjectMenu
    {
        private const string IgnoreMenu = "Assets/JulyGF/资源检查/忽略文件夹";
        private const string RestoreMenu = "Assets/JulyGF/资源检查/取消忽略文件夹";

        private static AssetCheckWindow OpenWindow => Resources.FindObjectsOfTypeAll<AssetCheckWindow>().FirstOrDefault();
        private static string[] SelectedPaths => Selection.objects.Select(AssetDatabase.GetAssetPath).Distinct().ToArray();

        [MenuItem(IgnoreMenu, false, 2050)]
        private static void IgnoreFolders() => Execute(true);

        [MenuItem(RestoreMenu, false, 2051)]
        private static void RestoreFolders() => Execute(false);

        [MenuItem(IgnoreMenu, true)]
        private static bool ValidateIgnoreFolders() => ValidateOperation(true);

        [MenuItem(RestoreMenu, true)]
        private static bool ValidateRestoreFolders() => ValidateOperation(false);

        private static bool ValidateOperation(bool ignored)
        {
            if (!ValidateSelection()) return false;
            var file = new AssetCheckSettingsFile(Directory.GetParent(Application.dataPath).FullName);
            return CanChangeFolders(SelectedPaths, file.ReadExistingOrDefault(), ignored);
        }

        internal static bool CanChangeFolders(string[] paths, AssetCheckSettings settings, bool ignored)
        {
            var exclusions = new AssetCheckExclusions(settings);
            return paths.Any(path => ignored ? !exclusions.Contains(AssetDatabase.AssetPathToGUID(path), path) :
                exclusions.ContainsDirectly(AssetDatabase.AssetPathToGUID(path)));
        }

        internal static bool ValidateSelection()
        {
            var paths = SelectedPaths;
            return paths.Length > 0 && paths.All(path => path.StartsWith("Assets/", System.StringComparison.Ordinal) &&
                AssetDatabase.IsValidFolder(path)) && !(OpenWindow?.FolderOperationRunning ?? false);
        }

        private static void Execute(bool ignored)
        {
            var paths = SelectedPaths;
            var window = OpenWindow;
            if (window != null)
                window.SetFoldersIgnored(paths, ignored);
            else
                Apply(new AssetCheckSettingsFile(Directory.GetParent(Application.dataPath).FullName), paths, ignored);
        }

        // 无论结果窗口是否打开，都使用相同的工作区处理逻辑。
        internal static void Apply(AssetCheckSettingsFile file, string[] paths, bool ignored)
        {
            using (var workspace = new AssetCheckWorkspace(file))
                workspace.SetFoldersIgnored(paths, ignored);
        }
    }
}
