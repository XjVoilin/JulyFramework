using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.Rendering.URP.Editor
{
    internal sealed class MatCapTextureEdit : ScriptableObject, IDisposable
    {
        [SerializeField] Texture2D texture;
        [SerializeField] bool followsMaterial;
        [SerializeField] bool newReflection;
        [SerializeField] List<MatCapLightCard> lightCards = new List<MatCapLightCard>();
        [SerializeField] internal float fill;
        [SerializeField] internal float strength;
        [SerializeField] internal float darkRetention;

        MatCapRecipe baseline;
        Texture2D baked;
        bool dirty = true;

        internal Texture2D Texture => texture;
        internal bool FollowsMaterial { get => followsMaterial; set => followsMaterial = value; }
        internal bool IsNewReflection => newReflection;
        internal bool IsBuiltInTexture { get; private set; }
        internal List<MatCapLightCard> LightCards => lightCards;
        internal bool UsesReflectionCards => baseline != null;
        internal bool HasDraft => UsesReflectionCards && (fill != baseline.Fill || !lightCards.SequenceEqual(baseline.LightCards));
        internal bool CanGenerate => HasDraft && (lightCards.Count > 0 || fill > 0);

        internal static MatCapTextureEdit Create()
        {
            var edit = CreateInstance<MatCapTextureEdit>();
            edit.hideFlags = HideFlags.HideAndDontSave;
            return edit;
        }

        internal void SetSource(Texture2D image, bool followsMaterial = false, bool newReflection = false)
        {
            texture = image;
            this.followsMaterial = followsMaterial;
            this.newReflection = newReflection;
            ReloadBaseline();
            Restore();
        }

        internal void ReloadBaseline()
        {
            IsBuiltInTexture = false;
            if (newReflection) baseline = MatCapRecipe.FromLightCards(Array.Empty<MatCapLightCard>(), false, 0f);
            else
            {
                var preset = MatCapPresetCatalog.TexturePresetFor(texture);
                bool isDefault = MatCapTextureAssets.IsDefault(texture);
                IsBuiltInTexture = preset != null || isDefault;
                if (preset != null) baseline = MatCapRecipe.LoadFile(preset.RecipeFile);
                else if (isDefault) baseline = MatCapRecipe.LoadDefault();
                else baseline = MatCapRecipe.Load(texture);
            }
            Invalidate();
        }

        internal void Restore()
        {
            fill = baseline?.Fill ?? 0f;
            lightCards.Clear();
            if (UsesReflectionCards) lightCards.AddRange(baseline.LightCards);
            Invalidate();
        }

        internal void Invalidate() => dirty = true;

        internal Texture2D Result()
        {
            if (!UsesReflectionCards) return texture;
            if (!dirty && baked != null) return baked;
            Object.DestroyImmediate(baked);
            baked = Bake(lightCards);
            dirty = false;
            return baked;
        }

        internal Texture2D RenderWithoutLight(int index)
        {
            var lights = lightCards.ToArray();
            lights[index].hidden = true;
            return Bake(lights);
        }

        Texture2D Bake(IReadOnlyList<MatCapLightCard> lights) =>
            MatCapReflectionBaker.Bake(lights, MatCapRecipe.GeneratedSize, baseline.Srgb, fill);

        internal Texture2D Save(string path)
        {
            var recipe = MatCapRecipe.FromLightCards(lightCards, baseline.Srgb, fill);
            return MatCapTextureAssets.SaveNew(path, Result(), recipe);
        }

        public void Dispose()
        {
            Undo.ClearUndo(this);
            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(this);
        }
    }
}
