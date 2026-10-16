using System;
using UnityEditor;

namespace July.AssetCheck
{
    internal static class UrpLightMode
    {
        // 保持业务模型和 JSON 的枚举值不变；URP 自身采用不同的枚举顺序。
        internal static int Encode(AdditionalLightStandard value)
        {
            switch (value)
            {
                case AdditionalLightStandard.Disabled: return 0;
                case AdditionalLightStandard.PerVertex: return 2;
                case AdditionalLightStandard.PerPixel: return 1;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
        internal static AdditionalLightStandard Decode(int value)
        {
            switch (value)
            {
                case 0: return AdditionalLightStandard.Disabled;
                case 2: return AdditionalLightStandard.PerVertex;
                case 1: return AdditionalLightStandard.PerPixel;
                default: throw new NotSupportedException("不支持的 URP 光源模式：" + value);
            }
        }
    }

    public sealed class UrpFacts
    {
        public bool Hdr { get; internal set; }
        public bool Depth { get; internal set; }
        public bool Opaque { get; internal set; }
        public bool SrpBatcher { get; internal set; }
        public bool MainShadowsConfigured { get; internal set; }
        public bool AdditionalShadowsConfigured { get; internal set; }
        public bool SoftShadows { get; internal set; }
        public int Msaa { get; internal set; }
        public int MainShadowResolution { get; internal set; }
        public int Cascades { get; internal set; }
        public float RenderScale { get; internal set; }
        public float ShadowDistance { get; internal set; }
        public AdditionalLightStandard MainLight { get; internal set; }
        public AdditionalLightStandard AdditionalLights { get; internal set; }
        public bool HasLightingRenderer { get; internal set; }
        public bool HasConfigurableLights { get; internal set; }
        public bool HasForwardPlus { get; internal set; }
        public bool CapabilitiesKnown { get; internal set; } = true;
        public bool MainShadows => CapabilitiesKnown && HasLightingRenderer && MainLight == AdditionalLightStandard.PerPixel && MainShadowsConfigured;
        public bool CanCastAdditionalShadows => CapabilitiesKnown && HasLightingRenderer &&
            AdditionalLights != AdditionalLightStandard.PerVertex &&
            (HasForwardPlus || AdditionalLights == AdditionalLightStandard.PerPixel);
        public bool AdditionalShadows => CanCastAdditionalShadows && AdditionalShadowsConfigured;
        public bool AnyShadows => MainShadows || AdditionalShadows;

        private static readonly string[] BooleanFields = { "m_SupportsHDR", "m_RequireDepthTexture", "m_RequireOpaqueTexture",
            "m_UseSRPBatcher", "m_MainLightShadowsSupported", "m_AdditionalLightShadowsSupported", "m_SoftShadowsSupported" };
        private static readonly string[] IntegerFields = { "m_MSAA", "m_MainLightShadowmapResolution", "m_ShadowCascadeCount",
            "m_MainLightRenderingMode", "m_AdditionalLightsRenderingMode", "m_DefaultRendererIndex" };

        internal static bool TryRead(SerializedObject source, out UrpFacts facts, out string reason)
        {
            facts = null;
            foreach (var field in BooleanFields)
                if (source.FindProperty(field)?.propertyType != SerializedPropertyType.Boolean)
                { reason = "缺少或不支持的布尔字段：" + field; return false; }
            foreach (var field in IntegerFields)
            {
                var property = source.FindProperty(field);
                if (property == null || (property.propertyType != SerializedPropertyType.Integer && property.propertyType != SerializedPropertyType.Enum))
                { reason = "缺少或不支持的整数字段：" + field; return false; }
            }
            if (source.FindProperty("m_RenderScale")?.propertyType != SerializedPropertyType.Float ||
                source.FindProperty("m_ShadowDistance")?.propertyType != SerializedPropertyType.Float ||
                source.FindProperty("m_RendererDataList") == null)
            { reason = "无法读取 URP 比例、阴影距离或 Renderer 列表"; return false; }

            var main = source.FindProperty("m_MainLightRenderingMode").intValue;
            var additional = source.FindProperty("m_AdditionalLightsRenderingMode").intValue;
            if (main < 0 || main > 2 || additional < 0 || additional > 2)
            { reason = "不支持的 URP 光源模式编码"; return false; }
            facts = new UrpFacts
            {
                Hdr = source.FindProperty("m_SupportsHDR").boolValue,
                Depth = source.FindProperty("m_RequireDepthTexture").boolValue,
                Opaque = source.FindProperty("m_RequireOpaqueTexture").boolValue,
                SrpBatcher = source.FindProperty("m_UseSRPBatcher").boolValue,
                MainShadowsConfigured = source.FindProperty("m_MainLightShadowsSupported").boolValue,
                AdditionalShadowsConfigured = source.FindProperty("m_AdditionalLightShadowsSupported").boolValue,
                SoftShadows = source.FindProperty("m_SoftShadowsSupported").boolValue,
                Msaa = source.FindProperty("m_MSAA").intValue,
                MainShadowResolution = source.FindProperty("m_MainLightShadowmapResolution").intValue,
                Cascades = source.FindProperty("m_ShadowCascadeCount").intValue,
                RenderScale = source.FindProperty("m_RenderScale").floatValue,
                ShadowDistance = source.FindProperty("m_ShadowDistance").floatValue,
                MainLight = UrpLightMode.Decode(main), AdditionalLights = UrpLightMode.Decode(additional)
            };
            var renderers = source.FindProperty("m_RendererDataList");
            for (var index = 0; index < renderers.arraySize; index++)
            {
                var renderer = renderers.GetArrayElementAtIndex(index).objectReferenceValue;
                if (renderer == null) continue; // 由失效 Renderer 引用检查单独报告。
                var type = renderer.GetType().FullName;
                if (type == "UnityEngine.Rendering.Universal.Renderer2DData") continue;
                if (type != "UnityEngine.Rendering.Universal.UniversalRendererData")
                { facts.CapabilitiesKnown = false; continue; }
                using (var serialized = new SerializedObject(renderer))
                {
                    var mode = serialized.FindProperty("m_RenderingMode");
                    if (mode == null || mode.intValue < 0 || mode.intValue > 2)
                    { facts.CapabilitiesKnown = false; continue; }
                    facts.HasLightingRenderer = true;
                    facts.HasForwardPlus |= mode.intValue == 2;
                    facts.HasConfigurableLights |= mode.intValue != 2;
                }
            }
            reason = null;
            return true;
        }
    }
}
