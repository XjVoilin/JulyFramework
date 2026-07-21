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
                else
                {
                    var missing = result.missingSprites != null && result.missingSprites.Count > 0
                        ? $"\n\n缺失文件:\n{string.Join("\n", result.missingSprites)}"
                        : "";
                    EditorUtility.DisplayDialog("Art Importer", $"导入失败。{missing}", "确定");
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
            var result = new ImportResult { missingSprites = new List<string>() };

            if (!Directory.Exists(sourceFolderPath))
            {
                Debug.LogError($"ArtImporter: 源文件夹不存在 → {sourceFolderPath}");
                return result;
            }

            // 查找 JSON 文件
            var jsonPath = FindJsonFile(sourceFolderPath);
            if (string.IsNullOrEmpty(jsonPath))
            {
                Debug.LogError($"ArtImporter: 未找到支持的 JSON 文件（*_ui_data.json / *_ps_data.json / *_figma_data.json）");
                return result;
            }

            var panelName = ExtractPanelName(jsonPath);
            if (string.IsNullOrEmpty(panelName))
            {
                Debug.LogError($"ArtImporter: 无法从 JSON 文件名提取面板名 → {jsonPath}");
                return result;
            }

            // 目标面板目录
            var targetPanelDir = $"{targetArtTexturesBase}/{panelName}";
            EnsureDirectoryExists(targetPanelDir);

            // 解析 JSON 中的 sprite 引用
            var jsonText = File.ReadAllText(jsonPath);
            var spriteNames = ExtractSpriteNames(jsonText);

            // 复制本地 sprites（无 /）
            foreach (var name in spriteNames)
            {
                if (name.Contains("/")) continue;

                var srcFile = Path.Combine(sourceFolderPath, name + ".png");
                var dstFile = $"{targetPanelDir}/{name}.png";
                var dstAbsolute = Path.GetFullPath(dstFile);

                if (File.Exists(srcFile))
                {
                    File.Copy(srcFile, dstAbsolute, true);
                    result.localCopied++;
                }
                else
                {
                    result.missingSprites.Add(name + ".png");
                }
            }

            // 复制共享 sprites（有 /）
            foreach (var name in spriteNames)
            {
                if (!name.Contains("/")) continue;

                var srcResolved = ResolveSharedSpriteSource(sourceFolderPath, name);
                var dstFile = $"{targetArtTexturesBase}/{name}.png";
                var dstAbsolute = Path.GetFullPath(dstFile);

                if (!string.IsNullOrEmpty(srcResolved) && File.Exists(srcResolved))
                {
                    var dstDir = Path.GetDirectoryName(dstAbsolute);
                    if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
                        Directory.CreateDirectory(dstDir);

                    File.Copy(srcResolved, dstAbsolute, true);
                    result.sharedCopied++;
                }
                else
                {
                    result.missingSprites.Add(name + ".png");
                }
            }

            // 复制 JSON 到面板目录
            var jsonFileName = Path.GetFileName(jsonPath);
            var jsonDst = Path.GetFullPath($"{targetPanelDir}/{jsonFileName}");
            File.Copy(jsonPath, jsonDst, true);

            // 刷新 AssetDatabase
            AssetDatabase.Refresh();

            // 生成 Prefab
            var unityJsonPath = $"{targetPanelDir}/{jsonFileName}";
            PS2UGUIGenerator.GenerateFromJson(unityJsonPath);

            result.success = true;
            result.prefabPath = unityJsonPath;
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

        /// <summary>
        /// 从 JSON 内容提取所有 image 类型 layer 的 name 字段作为 sprite 引用。
        /// </summary>
        private static HashSet<string> ExtractSpriteNames(string jsonText)
        {
            var names = new HashSet<string>();
            var data = LitJson.JsonMapper.ToObject(jsonText);
            if (data == null || !data.ContainsKey("layers")) return names;

            var layers = data["layers"];
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (!layer.ContainsKey("type") || !layer.ContainsKey("name")) continue;
                var type = (string)layer["type"];
                if (type != "image") continue;

                var name = (string)layer["name"];
                if (string.IsNullOrEmpty(name) || name.StartsWith("#")) continue;

                // Strip special suffixes for file lookup
                var stripped = StripSpecialSuffix(name);
                if (!string.IsNullOrEmpty(stripped))
                    names.Add(stripped);
            }
            return names;
        }

        private static string StripSpecialSuffix(string name)
        {
            // Strip *scale suffix
            var starIdx = name.LastIndexOf('*');
            if (starIdx > 0) name = name.Substring(0, starIdx);
            // Strip $ % @ suffixes
            if (name.Length > 0 && (name[name.Length - 1] == '$' || name[name.Length - 1] == '%' || name[name.Length - 1] == '@'))
                name = name.Substring(0, name.Length - 1);
            return name;
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
