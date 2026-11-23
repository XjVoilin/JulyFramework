using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace July.Rendering.URP.Editor
{
    internal static class MatCapTextureAssets
    {
        const string DefaultTextureGuid = "848cad05a3dd418389bd3a8df9cbac47"; // Matcap_Default.png
        internal const string TexturePrefix = "Matcap_";
        internal static Texture2D DefaultTexture => MatCapPresetCatalog.LoadAsset<Texture2D>(DefaultTextureGuid);

        internal static bool IsDefault(Texture2D texture) =>
            texture != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture)) == DefaultTextureGuid;

        internal static IEnumerable<Texture2D> FindProjectTextures()
        {
            foreach (string guid in AssetDatabase.FindAssets($"{TexturePrefix} t:Texture2D", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileName(path).StartsWith(TexturePrefix, StringComparison.Ordinal)) continue;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null) yield return texture;
            }
        }

        internal static Texture2D MakeRgbTexture(int size, bool srgb, byte[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false, !srgb)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(pixels);
            texture.Apply(false);
            return texture;
        }

        internal static Texture2D SaveNew(string path, Texture2D image, MatCapRecipe recipe)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !Path.GetFileName(path).StartsWith(TexturePrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("请在 Assets 中保存，贴图名称以 Matcap_ 开头。");
            if (File.Exists(path) || File.Exists(path + ".meta"))
                throw new InvalidOperationException("贴图已存在，请为新 MatCap 使用另一个名称。");

            bool createdImage = false;
            bool saved = false;
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    createdImage = true;
                    byte[] png = image.EncodeToPNG();
                    stream.Write(png, 0, png.Length);
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = recipe.Srgb;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = Mathf.Max(MatCapRecipe.GeneratedSize, Mathf.NextPowerOfTwo(Mathf.Max(image.width, image.height)));
                importer.SaveAndReimport();
                var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (imported == null) throw new InvalidOperationException("生成的 PNG 未能导入为 Texture2D。");
                recipe.Save(AssetDatabase.AssetPathToGUID(path));
                saved = true;
                return imported;
            }
            finally
            {
                if (!saved && createdImage && !AssetDatabase.DeleteAsset(path)) File.Delete(path);
            }
        }
    }
}
