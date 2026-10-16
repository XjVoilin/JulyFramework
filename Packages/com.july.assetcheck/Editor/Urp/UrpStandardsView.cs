using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static July.AssetCheck.AssetCheckStandardFields;

namespace July.AssetCheck
{
    internal static class UrpStandardsView
    {
        internal static void Build(VisualElement line, PlatformStandards standards, Action<Action<PlatformStandards>> edit)
        {
            var urp = standards.urp;
            AddPermission(line, "HDR", urp.allowHdr, v => edit(s => s.urp.allowHdr = v));
            AddPermission(line, "Depth Texture", urp.allowDepthTexture, v => edit(s => s.urp.allowDepthTexture = v));
            AddPermission(line, "Opaque Texture", urp.allowOpaqueTexture, v => edit(s => s.urp.allowOpaqueTexture = v));
            AddChoice(line, "MSAA 上限", new List<int> { 1, 2, 4, 8 }, urp.maxMsaa, v => edit(s => s.urp.maxMsaa = v));
            AddFloat(line, "Render Scale 上限", urp.maxRenderScale, v => edit(s => s.urp.maxRenderScale = v));
            AddRequired(line, "SRP Batcher", urp.requireSrpBatcher, v => edit(s => s.urp.requireSrpBatcher = v));
            AddPermission(line, "主光源阴影", urp.allowMainLightShadows, v => edit(s => s.urp.allowMainLightShadows = v));
            AddChoice(line, "主光阴影分辨率上限", new List<int> { 256, 512, 1024, 2048, 4096 }, urp.maxMainShadowResolution,
                v => edit(s => s.urp.maxMainShadowResolution = v));
            AddFloat(line, "阴影距离上限", urp.maxShadowDistance, v => edit(s => s.urp.maxShadowDistance = v));
            AddChoice(line, "阴影级联数上限", new List<int> { 1, 2, 3, 4 }, urp.maxShadowCascades, v => edit(s => s.urp.maxShadowCascades = v));
            AddPermission(line, "软阴影", urp.allowSoftShadows, v => edit(s => s.urp.allowSoftShadows = v));
            AddChoice(line, "附加光源模式", Enum.GetValues(typeof(AdditionalLightStandard)).Cast<AdditionalLightStandard>().ToList(),
                urp.additionalLights, v => edit(s => s.urp.additionalLights = v));
            AddPermission(line, "附加光源阴影", urp.allowAdditionalLightShadows, v => edit(s => s.urp.allowAdditionalLightShadows = v));
        }
    }
}
