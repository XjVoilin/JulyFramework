using UnityEngine;
using UnityEngine.Rendering;

namespace July.Rendering.URP.Editor
{
    internal static class MatCapMaterial
    {
        internal const string Texture = "_Matcap";
        internal const string Strength = "_MatcapScale";
        internal const string DarkRetention = "_MatcapDarkRetention";

        internal static bool IsCompatible(Material material) => GetError(material) == null;

        internal static string GetError(Material material)
        {
            if (material == null) return "未分配材质";
            if (material.shader == null) return "缺少 Shader";
            int index = Index(material, Texture);
            if (index < 0) return $"缺少 {Texture}";
            if (material.shader.GetPropertyType(index) != ShaderPropertyType.Texture ||
                material.shader.GetPropertyTextureDimension(index) != TextureDimension.Tex2D)
                return $"{Texture} 需要 2D 纹理";
            string strengthError = FloatError(material, Strength, required: true);
            return strengthError ?? FloatError(material, DarkRetention, required: false);
        }

        internal static bool HasDarkRetention(Material material) => Index(material, DarkRetention) >= 0;

        internal static float ReadDarkRetention(Material material) =>
            HasDarkRetention(material) ? material.GetFloat(DarkRetention) : 0;

        internal static void Apply(Material material, Texture2D image, float strengthValue, float darkRetentionValue)
        {
            material.SetTexture(Texture, image);
            material.SetFloat(Strength, strengthValue);
            if (HasDarkRetention(material)) material.SetFloat(DarkRetention, darkRetentionValue);
        }

        static string FloatError(Material material, string name, bool required)
        {
            int index = Index(material, name);
            if (index < 0) return required ? $"缺少 {name}" : null;
            var type = material.shader.GetPropertyType(index);
            return type == ShaderPropertyType.Float || type == ShaderPropertyType.Range
                ? null : $"{name} 需要 Float 或 Range";
        }

        internal static Vector2 StrengthRange(Material material)
        {
            int index = Index(material, Strength);
            return index >= 0 && material.shader.GetPropertyType(index) == ShaderPropertyType.Range
                ? material.shader.GetPropertyRangeLimits(index) : new Vector2(0, 2);
        }

        static int Index(Material material, string name) =>
            material != null && material.shader != null ? material.shader.FindPropertyIndex(name) : -1;
    }
}
