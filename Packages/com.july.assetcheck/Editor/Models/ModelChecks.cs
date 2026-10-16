using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    internal sealed class ModelImportOption
    {
        public string Id { get; }
        private readonly string _label;
        private readonly Func<ModelImporter, bool> _read;
        private readonly Func<ModelStandards, bool> _allowed;
        private readonly Action<ModelImporter> _disable;

        internal ModelImportOption(string id, string label, Func<ModelImporter, bool> read,
            Func<ModelStandards, bool> allowed, Action<ModelImporter> disable)
        {
            Id = id;
            _label = label;
            _read = read;
            _allowed = allowed;
            _disable = disable;
        }

        public void Check(ModelCheckContext context)
        {
            var facts = context;
            var standards = context.Standards;
            // 独立 Mesh 资源没有模型导入器，这些选项不适用于它们。
            if (facts.Importer != null && !_allowed(standards) && _read(facts.Importer))
                context.Add(new AssetCheckIssue(facts.AssetPath, AssetCheckTarget.Id(facts.AssetPath), AssetCheckKind.Model, Id,
                    _label, "开启", "禁止", true));
        }

        public void Fix(ModelImporter importer) => _disable(importer);
    }

    internal sealed class MeshTriangleRule : IAssetCheckRule<ModelCheckContext>
    {
        public string Id => "model.mesh-triangles";

        public void Check(ModelCheckContext context)
        {
            var facts = context;
            var standards = context.Standards;
            foreach (var mesh in facts.Meshes)
            {
                ulong triangles = 0;
                for (var index = 0; index < mesh.subMeshCount; index++)
                {
                    var topology = mesh.GetTopology(index);
                    if (topology == MeshTopology.Triangles)
                        triangles += mesh.GetIndexCount(index) / 3;
                    else if (topology == MeshTopology.Quads)
                        triangles += mesh.GetIndexCount(index) / 4 * 2;
                }
                if (triangles <= (ulong)standards.maxTrianglesPerMesh)
                    continue;

                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId);
                context.Add(new AssetCheckIssue(facts.AssetPath, guid, AssetCheckKind.Model, Id,
                    "Mesh 三角面数", triangles.ToString("N0"), "≤ " + standards.maxTrianglesPerMesh.ToString("N0"),
                    false, resourceName: facts.AssetPath + " / " + mesh.name, localId: localId));
            }
        }

    }

    internal sealed class ModelImportSettingsRule : IAssetCheckFix<ModelCheckContext>
    {
        private readonly ModelImportOption[] _options =
        {
            new ModelImportOption("model.read-write", "Read/Write", importer => importer.isReadable,
                standards => standards.allowReadWrite, importer => importer.isReadable = false),
            new ModelImportOption("model.import-cameras", "导入相机", importer => importer.importCameras,
                standards => standards.allowCameras, importer => importer.importCameras = false),
            new ModelImportOption("model.import-lights", "导入灯光", importer => importer.importLights,
                standards => standards.allowLights, importer => importer.importLights = false),
            new ModelImportOption("model.generate-colliders", "自动生成碰撞体", importer => importer.addCollider,
                standards => standards.allowGeneratedColliders, importer => importer.addCollider = false)
        };
        public void Check(ModelCheckContext context)
        {
            foreach (var option in _options) option.Check(context);
        }
        public void Fix(ModelCheckContext context, AssetCheckIssue issue) =>
            _options.Single(option => option.Id == issue.RuleId).Fix(context.Importer);
    }

    internal sealed class ModelCheckModule : IAssetCheckModule
    {
        private readonly IAssetCheckRule<ModelCheckContext>[] _rules = AssetCheckRules<ModelCheckContext>.Create();
        public AssetCheckKind Kind => AssetCheckKind.Model;
        public string RulesFingerprint => AssetCheckRules<ModelCheckContext>.Fingerprint;

        public string[] Collect(string scope) => AssetDatabase.FindAssets("t:Model", new[] { scope })
            .Concat(AssetDatabase.FindAssets("t:Mesh", new[] { scope })).Distinct().ToArray();

        public List<AssetCheckIssue> Check(string assetPath, PlatformStandards standards)
        {
            var issues = new List<AssetCheckIssue>();
            var context = new ModelCheckContext(assetPath, AssetImporter.GetAtPath(assetPath) as ModelImporter,
                AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().ToArray(), standards.model, issues);
            AssetCheckRules<ModelCheckContext>.Check(_rules, context);
            return issues;
        }

        public void Fix(string assetPath, IReadOnlyList<AssetCheckIssue> issues, PlatformStandards standards)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(assetPath);
            var context = new ModelCheckContext(assetPath, importer,
                AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().ToArray(), standards.model, new List<AssetCheckIssue>());
            foreach (var issue in issues) AssetCheckRules<ModelCheckContext>.Fix(context, issue);
            importer.SaveAndReimport();
        }
    }
}
