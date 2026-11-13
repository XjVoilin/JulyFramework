using July.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace July.UI.Authoring.Editor
{
    public struct ImportResult
    {
        public bool success;
        public bool cancelled;
        public string error;
        public string prefabPath;
        public int localCopied;
        public int sharedCopied;
        public List<string> missingSprites;
    }

    /// <summary>
    /// 从美术 Git 文件夹导入资源到 Unity 项目并生成 Prefab。
    /// 美术文件夹结构：{PanelName}/ 下包含 JSON + 同级 PNGs + 可能的共享目录。
    /// </summary>
    public class ArtImporter : EditorWindow
    {
        private string _sourceFolderPath = "";
        private int _targetIndex;
        private bool _isWindow;

        private string _customTargetPath = PS2UGUISettings.GetDefaultMiniGameTargetPath();

        private static string[] GetTargetLabels()
        {
            var boxPath = PS2UGUISettings.GetDefaultBoxTargetPath();
            return new[] { $"盒子 ({boxPath}/)", "小游戏 (自定义路径)" };
        }

        [MenuItem("Tools/PS2UGUI/Art Importer")]
        private static void Open()
        {
            var window = GetWindow<ArtImporter>("Art Importer");
            window.minSize = new Vector2(400, 220);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("从美术 Git 导入资源", EditorStyles.boldLabel);
            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            _sourceFolderPath = EditorGUILayout.TextField("源文件夹", _sourceFolderPath);
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                var path = EditorUtility.OpenFolderPanel("选择美术资源文件夹", _sourceFolderPath, "");
                if (!string.IsNullOrEmpty(path))
                    _sourceFolderPath = path;
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            _targetIndex = EditorGUILayout.Popup("目标位置", _targetIndex, GetTargetLabels());
            if (_targetIndex == 1)
            {
                _customTargetPath = EditorGUILayout.TextField("自定义路径", _customTargetPath);
            }

            GUILayout.Space(4);
            _isWindow = EditorGUILayout.Toggle("生成 UIWindow", _isWindow);

            GUILayout.Space(12);
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_sourceFolderPath));
            if (GUILayout.Button("导入并生成 Prefab", GUILayout.Height(30)))
            {
                var targetBase = _targetIndex == 0
                    ? PS2UGUISettings.GetDefaultBoxTargetPath()
                    : _customTargetPath.TrimEnd('/');

                var result = Import(_sourceFolderPath, targetBase, _isWindow);
                if (result.success)
                {
                    EditorUtility.DisplayDialog("Art Importer",
                        $"导入成功！\n\n本地 sprite: {result.localCopied}\n共享 sprite: {result.sharedCopied}\nPrefab: {result.prefabPath}",
                        "确定");
                }
                else if (!result.cancelled)
                {
                    var missing = result.missingSprites != null && result.missingSprites.Count > 0
                        ? $"\n\n缺失文件:\n{string.Join("\n", result.missingSprites)}"
                        : "";
                    EditorUtility.DisplayDialog("Art Importer", $"导入失败。\n{result.error}{missing}", "确定");
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        /// 程序化入口：从美术文件夹导入资源并生成 Prefab。
        /// </summary>
        /// <param name="sourceFolderPath">美术文件夹绝对路径（含 JSON + PNGs）</param>
        /// <param name="targetArtTexturesBase">Unity Art/Textures 根路径（不含尾 /）</param>
        /// <param name="isWindow">是否生成 UIWindow</param>
        public static ImportResult Import(string sourceFolderPath, string targetArtTexturesBase, bool isWindow = false)
        {
            try { return ImportCore(sourceFolderPath, targetArtTexturesBase, isWindow); }
            catch (Exception exception)
            {
                JLogger.LogError($"ArtImporter: {exception}");
                return new ImportResult { error = exception.Message, missingSprites = new List<string>() };
            }
        }

        private static ImportResult ImportCore(string sourceFolderPath, string targetArtTexturesBase, bool isWindow)
        {
            var result = new ImportResult { missingSprites = new List<string>() };

            if (!Directory.Exists(sourceFolderPath))
            {
                result.error = $"源文件夹不存在: {sourceFolderPath}";
                return result;
            }

            // 查找 JSON 文件
            var jsonPath = FindJsonFile(sourceFolderPath);
            if (string.IsNullOrEmpty(jsonPath))
            {
                result.error = "未找到支持的 JSON 文件（*_ui_data.json / *_ps_data.json / *_figma_data.json）";
                return result;
            }

            var panelName = ExtractPanelName(jsonPath);
            if (string.IsNullOrEmpty(panelName))
            {
                result.error = $"无法从 JSON 文件名提取面板名: {jsonPath}";
                return result;
            }

            targetArtTexturesBase = targetArtTexturesBase.Replace("\\", "/").TrimEnd('/');
            var assetsRoot = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            if (!targetArtTexturesBase.StartsWith("Assets/", StringComparison.Ordinal) ||
                !Path.GetFullPath(targetArtTexturesBase).StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("目标必须是 Assets/ 下的资源目录。", nameof(targetArtTexturesBase));
            var targetPanelDir = $"{targetArtTexturesBase}/{panelName}";
            var unityJsonPath = $"{targetPanelDir}/{Path.GetFileName(jsonPath)}";
            var prefabPath = PS2UGUIGenerator.ResolvePrefabOutputPath(unityJsonPath, panelName);
            if (string.IsNullOrEmpty(prefabPath))
            {
                result.error = $"无法确定 Prefab 输出路径，JSON 上级目录需要包含 Res/: {unityJsonPath}";
                return result;
            }

            // Translation owns name parsing. Resolve all source files before copying anything.
            var data = PS2UGUITranslate.Translate(jsonPath);
            var spriteNames = new HashSet<string>();
            CollectSpriteNames(data.children, spriteNames);
            var sourceFiles = new Dictionary<string, string>();
            foreach (var name in spriteNames)
            {
                var file = Path.Combine(sourceFolderPath, name + ".png");
                if (!File.Exists(file) && name.Contains("/")) file = ResolveSharedSpriteSource(sourceFolderPath, name);
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) result.missingSprites.Add(name + ".png");
                else sourceFiles.Add(name, file);
            }
            if (result.missingSprites.Count > 0)
            {
                result.error = "缺少导出图片，未复制资源或生成 Prefab。";
                return result;
            }
            if (!PS2UGUIGenerator.ConfirmOverwrite(prefabPath))
            {
                result.cancelled = true;
                return result;
            }
            EnsureDirectoryExists(targetPanelDir);
            foreach (var entry in sourceFiles)
            {
                var shared = entry.Key.Contains("/");
                var target = shared ? $"{targetArtTexturesBase}/{entry.Key}.png" : $"{targetPanelDir}/{entry.Key}.png";
                EnsureDirectoryExists(Path.GetDirectoryName(target));
                File.Copy(entry.Value, Path.GetFullPath(target), true);
                if (shared) result.sharedCopied++;
                else result.localCopied++;
            }

            // 复制 JSON 到面板目录
            var jsonFileName = Path.GetFileName(jsonPath);
            var jsonDst = Path.GetFullPath($"{targetPanelDir}/{jsonFileName}");
            File.Copy(jsonPath, jsonDst, true);

            // 刷新 AssetDatabase
            AssetDatabase.Refresh();

            // 生成 Prefab
            foreach (var name in spriteNames)
            {
                var assetPath = name.Contains("/") ? $"{targetArtTexturesBase}/{name}.png" : $"{targetPanelDir}/{name}.png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                }
            }
            var generated = PS2UGUIGenerator.GeneratePrefab(data, unityJsonPath, panelName, _ => true);
            result.success = generated.success;
            result.cancelled = generated.cancelled;
            result.error = generated.error;
            result.prefabPath = generated.prefabPath;
            return result;
        }

        private static string FindJsonFile(string folderPath)
        {
            string[] suffixes = { "_ui_data.json", "_ps_data.json", "_figma_data.json" };
            var files = Directory.GetFiles(folderPath, "*.json");
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                foreach (var suffix in suffixes)
                {
                    if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        return file;
                }
            }
            return null;
        }

        private static string ExtractPanelName(string jsonFilePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(jsonFilePath);
            string[] suffixes = { "_ui_data", "_ps_data", "_figma_data" };
            foreach (var suffix in suffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return fileName.Substring(0, fileName.Length - suffix.Length);
            }
            return fileName;
        }

        private static void CollectSpriteNames(List<UnityNode> nodes, HashSet<string> names)
        {
            foreach (var node in nodes)
            {
                if (!string.IsNullOrEmpty(node.spritePath)) names.Add(node.spritePath);
                if (!string.IsNullOrEmpty(node.fillSpritePath)) names.Add(node.fillSpritePath);
                CollectSpriteNames(node.children, names);
            }
        }

        /// <summary>
        /// 从源文件夹向上递归查找共享 sprite 的绝对路径。
        /// 逻辑同 PS2UGUIGenerator.ResolveSpriteFullPath 的有 / 分支。
        /// </summary>
        private static string ResolveSharedSpriteSource(string sourceFolderPath, string spriteName)
        {
            var firstSlash = spriteName.IndexOf('/');
            if (firstSlash < 0) return null;

            var firstSegment = spriteName.Substring(0, firstSlash);
            var dir = sourceFolderPath.TrimEnd('/', '\\');

            while (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, firstSegment);
                if (Directory.Exists(candidate))
                {
                    var fullFile = Path.Combine(dir, spriteName.Replace('/', Path.DirectorySeparatorChar) + ".png");
                    return fullFile;
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent;
            }

            return null;
        }

        private static void EnsureDirectoryExists(string assetPath)
        {
            var fullPath = Path.GetFullPath(assetPath);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }
    }
}
