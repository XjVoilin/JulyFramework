using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace July.Rendering.URP.Editor
{
    [Serializable]
    internal sealed class MatCapRecipe
    {
        const int CurrentVersion = 1;
        const string ReflectionGenerator = "reflection-cards";
        const string DefaultRecipeGuid = "0bcafd86c74a4b8a90cfe75ef658e76f"; // Matcap_Default.json
        internal const int GeneratedSize = 128;
        static readonly string ProjectRecipeDirectory = Path.Combine(MatCapAuthoringFiles.DirectoryPath, "Recipes");

        [SerializeField] int version = CurrentVersion;
        [SerializeField] int size;
        [SerializeField] bool srgb;
        [SerializeField] string generator;
        [SerializeField] List<MatCapLightCard> lightCards;
        [SerializeField] float fill;

        internal bool Srgb => srgb;
        internal IReadOnlyList<MatCapLightCard> LightCards => lightCards;
        internal float Fill => fill;

        internal static MatCapRecipe LoadDefault() => LoadFile(MatCapPresetCatalog.LoadAsset<TextAsset>(DefaultRecipeGuid));

        internal static MatCapRecipe LoadFile(TextAsset asset) => asset != null ? Parse(asset.text, AssetDatabase.GetAssetPath(asset)) : null;

        internal static MatCapRecipe Load(Texture2D texture)
        {
            if (texture == null) return null;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture));
            if (string.IsNullOrEmpty(guid)) return null;
            string path = RecipePath(guid);
            return File.Exists(path) ? Parse(File.ReadAllText(path), path) : null;
        }

        internal static IEnumerable<Texture2D> ProjectTextures()
        {
            if (!Directory.Exists(ProjectRecipeDirectory)) yield break;
            foreach (string recipe in Directory.EnumerateFiles(ProjectRecipeDirectory, "*.json"))
            {
                string path = AssetDatabase.GUIDToAssetPath(Path.GetFileNameWithoutExtension(recipe));
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null) yield return texture;
            }
        }

        static MatCapRecipe Parse(string json, string path)
        {
            try
            {
                var recipe = JsonUtility.FromJson<MatCapRecipe>(json);
                if (recipe != null && recipe.version == CurrentVersion && recipe.size > 0 && recipe.HasData())
                    return recipe;
                Debug.LogWarning($"无法读取 MatCap 配方：{path}。");
            }
            catch (ArgumentException error)
            {
                Debug.LogWarning($"无法读取 MatCap 配方：{path}。{error.Message}");
            }
            return null;
        }

        bool HasData() => generator == ReflectionGenerator && size <= MatCapReflectionBaker.MaxSize &&
            lightCards != null && lightCards.TrueForAll(card => Enum.IsDefined(typeof(MatCapLightShape), card.shape));

        internal static MatCapRecipe FromLightCards(IReadOnlyList<MatCapLightCard> cards, bool srgb, float fill) =>
            new MatCapRecipe
            {
                size = GeneratedSize, generator = ReflectionGenerator, srgb = srgb,
                lightCards = new List<MatCapLightCard>(cards), fill = fill
            };

        internal void Save(string textureGuid) => MatCapAuthoringFiles.Write(RecipePath(textureGuid), this, replace: false);

        static string RecipePath(string guid) => Path.Combine(ProjectRecipeDirectory, guid + ".json");
    }
}
