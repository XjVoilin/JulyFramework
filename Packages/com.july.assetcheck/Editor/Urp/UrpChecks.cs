using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.AssetCheck
{
    internal enum UrpConstraint { Allowed, Required, Maximum, Exact }

    internal enum UrpValueKind { Boolean, Integer, Float, LightMode }

    internal sealed class UrpValueCheck
    {
        internal readonly string Id;
        private readonly string _property;
        private readonly string _label;
        private readonly UrpConstraint _constraint;
        private readonly UrpValueKind _kind;
        private readonly Func<UrpFacts, double> _read;
        private readonly Func<UrpStandards, double> _standard;
        private readonly Func<UrpFacts, bool> _applies;

        internal UrpValueCheck(string id, string label, string property, UrpConstraint constraint, UrpValueKind kind,
            Func<UrpFacts, double> read, Func<UrpStandards, double> standard, Func<UrpFacts, bool> applies = null)
        {
            Id = "urp." + id;
            _label = label;
            _property = property;
            _constraint = constraint;
            _kind = kind;
            _read = read;
            _standard = standard;
            _applies = applies;
        }

        internal AssetCheckIssue Check(UrpFacts facts, string path, UrpStandards standards)
        {
            if (_applies != null && !_applies(facts)) return null;
            var actual = _read(facts);
            var expected = _standard(standards);
            var problem = _constraint == UrpConstraint.Allowed ? expected == 0 && actual != 0 :
                _constraint == UrpConstraint.Required ? expected != 0 && actual == 0 :
                _constraint == UrpConstraint.Maximum ? actual > expected : actual != expected;
            if (!problem) return null;
            var required = _constraint == UrpConstraint.Allowed ? "禁止" :
                _constraint == UrpConstraint.Required ? "开启" :
                (_constraint == UrpConstraint.Maximum ? "≤ " : string.Empty) + Format(expected);
            return new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.Urp, Id, _label,
                Format(actual), required, true);
        }

        internal void Fix(SerializedObject serialized, UrpFacts facts, UrpStandards standards)
        {
            if (Check(facts, AssetDatabase.GetAssetPath(serialized.targetObject), standards) == null) return;
            var property = serialized.FindProperty(_property);
            var value = _standard(standards);
            switch (_kind)
            {
                case UrpValueKind.Boolean: property.boolValue = value != 0; break;
                case UrpValueKind.Float: property.floatValue = (float)value; break;
                case UrpValueKind.LightMode: property.intValue = UrpLightMode.Encode((AdditionalLightStandard)(int)value); break;
                default: property.intValue = (int)value; break;
            }
        }

        private string Format(double value) => _kind == UrpValueKind.Boolean ? (value == 0 ? "关闭" : "开启") :
            _kind == UrpValueKind.LightMode ? ((AdditionalLightStandard)(int)value).ToString() : value.ToString("0.###");
    }

    internal sealed class UrpSettingsRule : IAssetCheckFix<UrpCheckContext>
    {
        private readonly UrpValueCheck[] _values =
        {
            new UrpValueCheck("hdr", "HDR", "m_SupportsHDR", UrpConstraint.Allowed, UrpValueKind.Boolean, f => f.Hdr ? 1 : 0, s => s.allowHdr ? 1 : 0),
            new UrpValueCheck("depth-texture", "Depth Texture", "m_RequireDepthTexture", UrpConstraint.Allowed, UrpValueKind.Boolean, f => f.Depth ? 1 : 0, s => s.allowDepthTexture ? 1 : 0),
            new UrpValueCheck("opaque-texture", "Opaque Texture", "m_RequireOpaqueTexture", UrpConstraint.Allowed, UrpValueKind.Boolean, f => f.Opaque ? 1 : 0, s => s.allowOpaqueTexture ? 1 : 0),
            new UrpValueCheck("msaa", "MSAA", "m_MSAA", UrpConstraint.Maximum, UrpValueKind.Integer, f => f.Msaa, s => s.maxMsaa),
            new UrpValueCheck("render-scale", "Render Scale", "m_RenderScale", UrpConstraint.Maximum, UrpValueKind.Float, f => f.RenderScale, s => s.maxRenderScale),
            new UrpValueCheck("srp-batcher", "SRP Batcher", "m_UseSRPBatcher", UrpConstraint.Required, UrpValueKind.Boolean, f => f.SrpBatcher ? 1 : 0, s => s.requireSrpBatcher ? 1 : 0),
            new UrpValueCheck("main-shadows", "主光源阴影", "m_MainLightShadowsSupported", UrpConstraint.Allowed, UrpValueKind.Boolean,
                f => f.MainShadowsConfigured ? 1 : 0, s => s.allowMainLightShadows ? 1 : 0, f => f.CapabilitiesKnown && f.HasLightingRenderer && f.MainLight == AdditionalLightStandard.PerPixel),
            new UrpValueCheck("main-shadow-resolution", "主光源阴影分辨率", "m_MainLightShadowmapResolution", UrpConstraint.Maximum, UrpValueKind.Integer, f => f.MainShadowResolution, s => s.maxMainShadowResolution, f => f.MainShadows),
            new UrpValueCheck("shadow-distance", "阴影距离", "m_ShadowDistance", UrpConstraint.Maximum, UrpValueKind.Float, f => f.ShadowDistance, s => s.maxShadowDistance, f => f.AnyShadows),
            new UrpValueCheck("shadow-cascades", "阴影级联数", "m_ShadowCascadeCount", UrpConstraint.Maximum, UrpValueKind.Integer, f => f.Cascades, s => s.maxShadowCascades, f => f.MainShadows),
            new UrpValueCheck("soft-shadows", "软阴影", "m_SoftShadowsSupported", UrpConstraint.Allowed, UrpValueKind.Boolean, f => f.SoftShadows ? 1 : 0, s => s.allowSoftShadows ? 1 : 0, f => f.AnyShadows),
            new UrpValueCheck("additional-lights", "附加光源模式", "m_AdditionalLightsRenderingMode", UrpConstraint.Exact, UrpValueKind.LightMode,
                f => (int)f.AdditionalLights, s => (int)s.additionalLights, f => f.CapabilitiesKnown && f.HasConfigurableLights),
            new UrpValueCheck("additional-shadows", "附加光源阴影", "m_AdditionalLightShadowsSupported", UrpConstraint.Allowed, UrpValueKind.Boolean,
                f => f.AdditionalShadowsConfigured ? 1 : 0, s => s.allowAdditionalLightShadows ? 1 : 0,
                f => f.CanCastAdditionalShadows)
        };
        public void Check(UrpCheckContext context)
        {
            var assetPath = context.AssetPath;
            if (!context.IsPipeline)
            {
                CheckFeatures(context);
                return;
            }
            var facts = context.Facts;
            if (!facts.CapabilitiesKnown)
                context.Add(Unverified(assetPath, "无法判断自定义 Renderer 的光照能力，相关光照规则未执行"));
            foreach (var value in _values)
            {
                var issue = value.Check(facts, assetPath, context.Standards);
                if (issue != null) context.Add(issue);
            }
            if (facts.CapabilitiesKnown && facts.HasForwardPlus && context.Standards.additionalLights != AdditionalLightStandard.PerPixel)
                context.Add(new AssetCheckIssue(assetPath, AssetCheckTarget.Id(assetPath), AssetCheckKind.Urp,
                    "urp.forward-plus-lights", "附加光源模式", "Forward+ 固定使用 PerPixel",
                    context.Standards.additionalLights.ToString(), false));
            CheckRendererList(context);
        }

        private void CheckRendererList(UrpCheckContext context)
        {
            var serialized = context.Serialized;
            var path = context.AssetPath;
            var list = serialized.FindProperty("m_RendererDataList");
            var index = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            if (index < 0 || index >= list.arraySize)
                context.Add(ReferenceIssue(path, "默认 Renderer 索引 " + index + " 无效", "m_DefaultRendererIndex"));
            for (var i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    context.Add(ReferenceIssue(path, "Renderer[" + i + "] 引用无效", "m_RendererDataList[" + i + "]"));
        }

        private void CheckFeatures(UrpCheckContext context)
        {
            var serialized = context.Serialized;
            var path = context.AssetPath;
            var features = serialized.FindProperty("m_RendererFeatures");
            if (features == null)
            {
                context.Add(Unverified(path, "无法读取 Renderer Feature 列表"));
                return;
            }
            for (var index = 0; index < features.arraySize; index++)
                if (features.GetArrayElementAtIndex(index).objectReferenceValue == null)
                    context.Add(ReferenceIssue(path, "Renderer Feature[" + index + "] 引用无效", "m_RendererFeatures[" + index + "]"));
        }

        private AssetCheckIssue ReferenceIssue(string path, string actual, string location) => new AssetCheckIssue(path,
            AssetCheckTarget.Id(path), AssetCheckKind.Urp, "urp.renderer-reference", "Renderer 配置有效性", actual, "有效索引及引用", false, location: location);
        private AssetCheckIssue Unverified(string path, string actual) => new AssetCheckIssue(path,
            AssetCheckTarget.Id(path), AssetCheckKind.Urp, "urp.schema", "未能验证", actual, "受支持的 URP 配置结构", false, true);
        public void Fix(UrpCheckContext context, AssetCheckIssue issue)
        {
            _values.Single(value => value.Id == issue.RuleId).Fix(context.Serialized, context.Facts, context.Standards);
        }
    }

    internal sealed class UrpCheckModule : IAssetCheckModule
    {
        private readonly IAssetCheckRule<UrpCheckContext>[] _rules = AssetCheckRules<UrpCheckContext>.Create();

        public AssetCheckKind Kind => AssetCheckKind.Urp;
        public string RulesFingerprint => AssetCheckRules<UrpCheckContext>.Fingerprint;

        public string[] Collect(string scope)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pipeline in ProjectRenderConfiguration.Pipelines().Where(ProjectRenderConfiguration.IsUrp).Distinct())
            {
                paths.Add(AssetDatabase.GetAssetPath(pipeline));
                using (var serialized = new SerializedObject(pipeline))
                {
                    var renderers = serialized.FindProperty("m_RendererDataList");
                    if (renderers == null)
                        continue; // 管线自身会在 Check 中报告不支持的序列化结构。
                    for (var index = 0; index < renderers.arraySize; index++)
                    {
                        var renderer = renderers.GetArrayElementAtIndex(index).objectReferenceValue;
                        if (renderer != null)
                            paths.Add(AssetDatabase.GetAssetPath(renderer));
                    }
                }
            }
            return paths.Select(AssetCheckTarget.Id).ToArray();
        }

        public List<AssetCheckIssue> Check(string assetPath, PlatformStandards standards)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            var issues = new List<AssetCheckIssue>();
            using (var serialized = new SerializedObject(asset))
            {
                var pipeline = ProjectRenderConfiguration.IsUrp(asset);
                UrpFacts facts = null;
                if (pipeline && !UrpFacts.TryRead(serialized, out facts, out var reason))
                {
                    issues.Add(Unverified(assetPath, reason));
                    return issues;
                }
                AssetCheckRules<UrpCheckContext>.Check(_rules,
                    new UrpCheckContext(serialized, assetPath, pipeline, facts, standards.urp, issues));
            }
            return issues;
        }

        private AssetCheckIssue Unverified(string path, string actual) => new AssetCheckIssue(path,
            AssetCheckTarget.Id(path), Kind, "urp.schema", "未能验证", actual, "受支持的 URP 配置结构", false, true);

        public void Fix(string assetPath, IReadOnlyList<AssetCheckIssue> issues, PlatformStandards standards)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            using (var serialized = new SerializedObject(asset))
            {
                Undo.RecordObject(asset, "修改资源检查 URP 配置");
                // 先修改光源模式，再处理阴影预算，保留因模式切换而不再生效的设置。
                var pipeline = ProjectRenderConfiguration.IsUrp(asset);
                foreach (var issue in issues.OrderBy(issue => issue.RuleId == "urp.additional-lights" ? 0 : 1))
                {
                    UrpFacts facts = null;
                    if (pipeline && !UrpFacts.TryRead(serialized, out facts, out var reason))
                        throw new NotSupportedException(reason);
                    AssetCheckRules<UrpCheckContext>.Fix(new UrpCheckContext(serialized, assetPath, pipeline,
                        facts, standards.urp, new List<AssetCheckIssue>()), issue);
                }
                serialized.ApplyModifiedProperties();
            }
            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }
}
