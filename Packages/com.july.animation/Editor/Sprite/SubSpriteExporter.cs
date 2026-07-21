using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.Animation.Editor
{
    /// <summary>
    /// 将 Sprite Mode: Multiple 的 Texture 中已切好的子 Sprite 导出为独立 PNG。
    /// 工作流：Sprite Editor 手动/自动切割 → 右键本工具导出 → 独立 PNG + 像素风 Import Settings。
    /// </summary>
    public static class SubSpriteExporter
    {
        [MenuItem("Assets/July/Animation/导出子 Sprite 为独立 PNG", false, 100)]
        public static void ExportSelected()
        {
            var textures = CollectSelectedTextures();
            if (textures.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请选中已切割为 Multiple 模式的 Texture2D 或包含它们的文件夹", "确定");
                return;
            }

            int totalExported = 0;
            int totalSkipped = 0;

            foreach (var path in textures)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                if (importer.spriteImportMode != SpriteImportMode.Multiple)
                {
                    Debug.Log($"[SubSpriteExporter] 跳过（非 Multiple 模式）: {Path.GetFileName(path)}");
                    totalSkipped++;
                    continue;
                }

                totalExported += ExportSubSprites(path, importer);
            }

            AssetDatabase.Refresh();

            var msg = $"导出 {totalExported} 个子 Sprite 为独立 PNG";
            if (totalSkipped > 0) msg += $"\n跳过 {totalSkipped} 个非 Multiple 模式的纹理";
            EditorUtility.DisplayDialog("完成", msg, "确定");
        }

        [MenuItem("Assets/July/Animation/导出子 Sprite 为独立 PNG", true)]
        private static bool ExportSelectedValidate() =>
            Selection.objects.Any(o => o is Texture2D or DefaultAsset);

        [MenuItem("Assets/July/Animation/批量设置像素风 Import Settings", false, 120)]
        public static void BatchConfigureImportSettings()
        {
            var textures = CollectSelectedTextures();
            if (textures.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请选中 Texture2D 或文件夹", "确定");
                return;
            }

            int changed = 0;
            foreach (var path in textures)
            {
                if (ApplyPixelArtImport(path)) changed++;
            }

            EditorUtility.DisplayDialog("完成",
                $"已配置 {changed}/{textures.Count} 个纹理\n(Sprite · PPU 16 · Point · Uncompressed · No Mipmap)",
                "确定");
        }

        [MenuItem("Assets/July/Animation/批量设置像素风 Import Settings", true)]
        private static bool BatchConfigureValidate() =>
            Selection.objects.Any(o => o is Texture2D or DefaultAsset);

        #region Export

        private static int ExportSubSprites(string assetPath, TextureImporter importer)
        {
            bool wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().ToArray();

            if (sprites.Length == 0)
            {
                Debug.LogWarning(
                    $"[SubSpriteExporter] {Path.GetFileName(assetPath)} 没有子 Sprite，请先在 Sprite Editor 中切割");
                RestoreReadable(importer, wasReadable);
                return 0;
            }

            var outputDir = Path.Combine(
                Path.GetDirectoryName(assetPath)!,
                Path.GetFileNameWithoutExtension(assetPath) + "_exported");

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            int exported = 0;

            foreach (var sprite in sprites)
            {
                var rect = sprite.rect;
                int x = Mathf.FloorToInt(rect.x);
                int y = Mathf.FloorToInt(rect.y);
                int w = Mathf.FloorToInt(rect.width);
                int h = Mathf.FloorToInt(rect.height);

                if (w <= 0 || h <= 0) continue;

                var pixels = tex.GetPixels(x, y, w, h);
                var output = new Texture2D(w, h, TextureFormat.RGBA32, false);
                output.SetPixels(pixels);
                output.Apply();

                var fileName = SanitizeFileName(sprite.name) + ".png";
                File.WriteAllBytes(Path.Combine(outputDir, fileName), output.EncodeToPNG());
                Object.DestroyImmediate(output);
                exported++;
            }

            RestoreReadable(importer, wasReadable);

            if (exported > 0)
            {
                AssetDatabase.Refresh();
                BatchApplyPixelArt(outputDir);
                Debug.Log(
                    $"[SubSpriteExporter] {Path.GetFileName(assetPath)} → {exported} 个子 Sprite → {outputDir}/");
            }

            return exported;
        }

        #endregion

        #region Import Settings

        private static bool ApplyPixelArtImport(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter imp) return false;

            bool changed = false;

            if (imp.textureType != TextureImporterType.Sprite)
            { imp.textureType = TextureImporterType.Sprite; changed = true; }

            if (Math.Abs(imp.spritePixelsPerUnit - 16) > 0.01f)
            { imp.spritePixelsPerUnit = 16; changed = true; }

            if (imp.filterMode != FilterMode.Point)
            { imp.filterMode = FilterMode.Point; changed = true; }

            if (imp.textureCompression != TextureImporterCompression.Uncompressed)
            { imp.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }

            if (imp.mipmapEnabled)
            { imp.mipmapEnabled = false; changed = true; }

            if (changed) imp.SaveAndReimport();
            return changed;
        }

        private static void BatchApplyPixelArt(string dir)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
                ApplyPixelArtImport(AssetDatabase.GUIDToAssetPath(guid));
        }

        #endregion

        #region Helpers

        private static List<string> CollectSelectedTextures()
        {
            var result = new List<string>();
            foreach (var obj in Selection.objects)
            {
                if (obj is Texture2D)
                {
                    result.Add(AssetDatabase.GetAssetPath(obj));
                }
                else if (obj is DefaultAsset)
                {
                    var folder = AssetDatabase.GetAssetPath(obj);
                    if (!AssetDatabase.IsValidFolder(folder)) continue;
                    foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                        result.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
            }

            return result;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static void RestoreReadable(TextureImporter importer, bool wasReadable)
        {
            if (!wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
        }

        #endregion
    }
}
