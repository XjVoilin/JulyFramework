using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    internal sealed class MaterialShaderRule : IAssetCheckRule<MaterialCheckContext>
    {
        public void Check(MaterialCheckContext context)
        {
            var material = context.Material;
            var path = context.AssetPath;
            if (material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId);
                context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.Material,
                    "material.missing-shader", "Shader 缺失", "无法解析 Shader", "有效 Shader", false,
                    resourceName: path + " / " + material.name, localId: localId));
            }
        }
    }

    internal sealed class MaterialReferenceRule : IAssetCheckRule<MaterialCheckContext>
    {
        public void Check(MaterialCheckContext context)
        {
            var material = context.Material;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId);
            SerializedReferenceChecks.Check(material, context, material.name, localId: localId,
                displayField: property =>
                {
                    const string suffix = ".second.m_Texture";
                    return property.propertyPath.EndsWith(suffix, StringComparison.Ordinal)
                        ? property.serializedObject.FindProperty(property.propertyPath.Substring(0,
                            property.propertyPath.Length - suffix.Length) + ".first").stringValue
                        : SerializedReferenceChecks.ReadableField(property.propertyPath);
                });
        }
    }

    internal sealed class MaterialCheckModule : IAssetCheckModule
    {
        private readonly IAssetCheckRule<MaterialCheckContext>[] _rules = AssetCheckRules<MaterialCheckContext>.Create();
        public AssetCheckKind Kind => AssetCheckKind.Material;
        public string RulesFingerprint => AssetCheckRules<MaterialCheckContext>.Fingerprint;
        public string[] Collect(string scope) => AssetDatabase.FindAssets("t:Material", new[] { scope });
        public List<AssetCheckIssue> Check(string assetPath, PlatformStandards standards)
        {
            var issues = new List<AssetCheckIssue>();
            foreach (var material in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>())
                AssetCheckRules<MaterialCheckContext>.Check(_rules, new MaterialCheckContext(material, assetPath, issues));
            return issues;
        }
        public void Fix(string assetPath, IReadOnlyList<AssetCheckIssue> issues, PlatformStandards standards) =>
            throw new InvalidOperationException("材质引用和 Shader 需要人工恢复，不能自动替换或清空。");
    }
}
