using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace July.Rendering.URP.Editor
{
    [Serializable]
    internal sealed class MatCapModelPreset
    {
        public string label;
        public string assetGuid;
        public string materialGuid;

        internal GameObject Model => MatCapPresetCatalog.LoadAsset<GameObject>(assetGuid);
        internal Material Material => MatCapPresetCatalog.LoadAsset<Material>(materialGuid);
    }

    [Serializable]
    internal sealed class MatCapTexturePreset
    {
        public string textureGuid;
        public string recipeGuid;

        internal Texture2D Texture => MatCapPresetCatalog.LoadAsset<Texture2D>(textureGuid);
        internal TextAsset RecipeFile => MatCapPresetCatalog.LoadAsset<TextAsset>(recipeGuid);
    }

    [Serializable]
    internal sealed class MatCapPresetCatalog
    {
        const string PresetCatalogGuid = "e0862698ae4d806429eab847d9e1e62f"; // models.json

        [Serializable]
        sealed class ReferenceMaterial
        {
            public string materialGuid;
            public float darkRetention;
        }

        static MatCapPresetCatalog builtIn;
        [SerializeField] List<MatCapModelPreset> models;
        [SerializeField] List<MatCapTexturePreset> texturePresets;
        [SerializeField] List<ReferenceMaterial> referenceMaterials;

        internal static IReadOnlyList<MatCapModelPreset> BuiltIn => (builtIn ??= Load()).models;
        internal static IReadOnlyList<MatCapTexturePreset> TexturePresets => (builtIn ??= Load()).texturePresets;

        internal static MatCapModelPreset ModelPresetFor(GameObject model)
        {
            if (model == null) return null;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(model));
            return (builtIn ??= Load()).models.Find(preset => preset.assetGuid == guid);
        }

        internal static MatCapTexturePreset TexturePresetFor(Texture2D texture)
        {
            if (texture == null) return null;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture));
            if (string.IsNullOrEmpty(guid)) return null;
            return (builtIn ??= Load()).texturePresets.Find(entry => entry.textureGuid == guid);
        }

        internal static T LoadAsset<T>(string guid) where T : UnityEngine.Object => string.IsNullOrEmpty(guid) ? null :
            AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

        internal static float? DarkRetentionFor(Material material)
        {
            if (material == null) return null;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material));
            return (builtIn ??= Load()).referenceMaterials?.Find(entry => entry.materialGuid == guid)?.darkRetention;
        }

        static MatCapPresetCatalog Load()
        {
            var catalog = LoadAsset<TextAsset>(PresetCatalogGuid);
            if (catalog == null) throw new InvalidOperationException($"找不到 July MatCap 预设目录 models.json（GUID {PresetCatalogGuid}）。");
            return JsonUtility.FromJson<MatCapPresetCatalog>(catalog.text);
        }
    }
}
