using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;
using static July.AssetCheck.Tests.BundleAnalysisPanelTests;

namespace July.AssetCheck.Tests
{
    public sealed class BundleInspectionTests
    {
        private string _root, _temp, _report;
        [SetUp] public void Setup()
        {
            _root = "Assets/__BundleInspection_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _temp = Path.GetFullPath("Temp/BundleInspection_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_temp); _report = Path.Combine(_temp, "fixture.report");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_temp, AssetCheckSettingsFile.FileName)));
        }
        [TearDown] public void Cleanup() { AssetDatabase.DeleteAsset(_root); Assert.That(_temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar)); Directory.Delete(_temp, true); }
        private string Sprite(string name)
        {
            var path = _root + "/" + name + ".png"; var t = new Texture2D(8, 8); File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.Sprite; importer.mipmapEnabled = true; importer.SaveAndReimport(); return path;
        }
        [Test] public void DiamondCyclesSharedEdgesAndSizeBoundariesHaveDistinctCorrectTotals()
        {
            Write(_report, new[] { Bundle("A", 32768, new[] { "B", "C" }), Bundle("B", 100, new[] { "D" }), Bundle("C", 200, new[] { "D" }), Bundle("D", 3145728, Array.Empty<string>()), Bundle("X", 50, new[] { "Y" }), Bundle("Y", 60, new[] { "Z" }), Bundle("Z", 70, new[] { "X" }), Bundle("self", 80, new[] { "self" }) });
            var graph = new BundlePackageAnalysis(BundleBuildReport.Read(_report)); var settings = AssetCheckSettings.CreateDefault();
            Assert.That(graph.Dependencies("A"), Is.EquivalentTo(new[] { "B", "C", "D" })); Assert.That(graph.TotalBytes("A"), Is.EqualTo(32768L + 100 + 200 + 3145728));
            Assert.That(graph.Analysis.References("D"), Is.EquivalentTo(new[] { "B", "C" })); Assert.That(graph.Cyclic("A"), Is.False);
            Assert.That(graph.CyclePath("X"), Is.EqualTo(new[] { "X", "Y", "Z", "X" })); Assert.That(graph.CyclePath("self"), Is.EqualTo(new[] { "self", "self" }));
            Assert.That(graph.SizeMark("A", settings), Is.Empty); Assert.That(graph.SizeMark("D", settings), Is.Empty); Assert.That(graph.SizeMark("B", settings), Is.EqualTo("过小"));
            settings.maxBundleBytes--; Assert.That(graph.SizeMark("D", settings), Is.EqualTo("过大")); Assert.That(graph.Causes("X", "Y"), Is.Empty);
        }
        [Test] public void ReportResourceEvidenceDoesNotConsultCurrentDependencies()
        {
            var a = new BundleReportAssetData { AssetPath = "Assets/old.prefab", AssetGUID = new string('a', 32), MainBundleName = "A", DependBundles = new[] { "B" }, DependAssets = new[] { new BundleReportDependencyData { AssetPath = "Assets/old.mat", AssetGUID = new string('b', 32) } } };
            var b = new BundleReportAssetData { AssetPath = "Assets/old.mat", AssetGUID = new string('b', 32) };
            Write(_report, new[] { Bundle("A", 100, new[] { "B" }, a), Bundle("B", 200, new[] { "A" }, b) }, a);
            var graph = new BundlePackageAnalysis(BundleBuildReport.Read(_report)); Assert.That(graph.Causes("A", "B").Single().Dependency.Path, Is.EqualTo("Assets/old.mat"));
            Assert.That(graph.Causes("B", "A"), Is.Empty); Assert.That(graph.Causes("A", "B").Single().Source.ResolveCurrentPath(), Is.Empty);
        }
        [Test] public void ReportDependencyRecordsRoundTripWithoutSerializationDepthWarnings()
        {
            var warnings = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (message.Contains("Serialization depth")) warnings.Add(message); };
            Application.logMessageReceived += capture;
            try
            {
                var dependency = new BundleReportDependencyData { AssetPath = "Assets/dependency.mat", AssetGUID = new string('b', 32) };
                var main = new BundleReportAssetData { AssetPath = "Assets/main.prefab", MainBundleName = "A", DependBundles = Array.Empty<string>(), DependAssets = new[] { dependency } };
                Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), main) }, main);
                var actual = BundleBuildReport.Read(_report).Data.AssetInfos.Single().DependAssets.Single();
                Assert.That(actual.AssetPath, Is.EqualTo(dependency.AssetPath)); Assert.That(actual.AssetGUID, Is.EqualTo(dependency.AssetGUID));
                Assert.That(warnings, Is.Empty);
            }
            finally { Application.logMessageReceived -= capture; }
        }
        [Test] public void ExplicitInputIgnoresOrdinaryScopeAndExclusionsWithoutOverwritingItsReports()
        {
            var a = Sprite("one"); var b = Sprite("two"); var notIncluded = Sprite("outside-report");
            var x = Asset(a); var y = Asset(b); Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), x, y), Bundle("B", 200, Array.Empty<string>(), x) });
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_temp)))
            {
                workspace.ChangeSettings(s => s.scope = _root); workspace.SetFoldersIgnored(new[] { _root }, true); workspace.StartScan(AssetCheckKind.Texture);
                while (workspace.Running) workspace.Step(); var ordinary = workspace.GetReport(AssetCheckKind.Texture); Assert.That(ordinary.IgnoredFiles, Is.EqualTo(3));
                var before = File.ReadAllBytes(Path.Combine(_temp, AssetCheckSettingsFile.FileName));
                using (var session = new BundleInspectionSession(BundleBuildReport.Read(_report), workspace.Settings, () => false))
                {
                    Finish(session); Assert.That(session.Resources.Length, Is.EqualTo(2)); Assert.That(session.Resources.All(r => r.Checked), Is.True);
                    Assert.That(session.Resources.All(r => r.Issues.Any(i => i.RuleId == "texture.sprite-mipmaps")), Is.True);
                    Assert.That(session.Resources.All(r => r.SameContent.Length == 2), Is.True); Assert.That(session.Resources.Any(r => r.CheckedPath == notIncluded), Is.False);
                    Assert.That(session.Resources.Single(r => r.CheckedPath == a).Asset.Owners.Count, Is.EqualTo(2));
                }
                Assert.That(workspace.GetReport(AssetCheckKind.Texture), Is.SameAs(ordinary)); Assert.That(ordinary.IgnoredFiles, Is.EqualTo(3)); Assert.That(File.ReadAllBytes(Path.Combine(_temp, AssetCheckSettingsFile.FileName)), Is.EqualTo(before));
            }
        }
        [Test] public void RulesAreSharedAndAtlasSettingsSupersedePackedSpriteMipMaps()
        {
            var sprite = Sprite("sprite"); var atlas = new SpriteAtlas(); atlas.Add(new Object[] { AssetDatabase.LoadAssetAtPath<Texture2D>(sprite) });
            var textureSettings = atlas.GetTextureSettings(); textureSettings.generateMipMaps = true; atlas.SetTextureSettings(textureSettings);
            var atlasPath = _root + "/atlas.spriteatlas"; AssetDatabase.CreateAsset(atlas, atlasPath);
            var standards = AssetCheckSettings.CreateDefault().CurrentStandards; standards.texture.requireUncompressedAtlasSources = false;
            var module = new TextureCheckModule(); Assert.That(module.Check(sprite, standards).Any(i => i.RuleId == "texture.sprite-mipmaps"), Is.False);
            Assert.That(module.Check(atlasPath, standards).Any(i => i.RuleId == "atlas.mipmaps"), Is.True);
            standards.texture.allowAtlasMipMaps = true; Assert.That(module.Check(atlasPath, standards).Any(i => i.RuleId == "atlas.mipmaps"), Is.False);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, subMeshCount = 5 };
            for (var i = 0; i < 5; i++) mesh.SetTriangles(new[] { 0, 1, 2 }, i);
            var meshPath = _root + "/mesh.asset"; AssetDatabase.CreateAsset(mesh, meshPath); var models = new ModelCheckModule();
            Assert.That(models.Check(meshPath, standards).Single(i => i.RuleId == "model.mesh-submeshes").CanFix, Is.False);
            standards.model.maxSubMeshes = 5; Assert.That(models.Check(meshPath, standards).Any(i => i.RuleId == "model.mesh-submeshes"), Is.False);
        }

        [Test] public void ChangedInputsStopChecksButKeepReportAndMissingAssetsDoNotPass()
        {
            var x = new BundleReportAssetData { AssetPath = _root + "/gone.png", AssetGUID = new string('a', 32) }; Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), x) });
            using (var session = new BundleInspectionSession(BundleBuildReport.Read(_report), AssetCheckSettings.CreateDefault(), () => false)) { Finish(session); Assert.That(session.Resources.Single().Status, Is.EqualTo("未能检查")); }
            using (var session = new BundleInspectionSession(BundleBuildReport.Read(_report), AssetCheckSettings.CreateDefault(), () => true))
            { session.Step(); Assert.That(session.Complete, Is.False); Assert.That(session.Running, Is.False); Assert.That(session.Packages.Report.TotalBytes, Is.EqualTo(100)); Assert.That(session.Resources.Single().Checked, Is.False); }
        }
        [Test] public void ContentComparisonDoesNotTreatNamesOrMetadataAsSourceContent()
        {
            var a = Path.Combine(_temp, "one.bin"); var b = Path.Combine(_temp, "two.bin"); var c = Path.Combine(_temp, "different.bin");
            File.WriteAllBytes(a, new byte[] { 1, 2, 3 }); File.WriteAllBytes(b, new byte[] { 1, 2, 3 }); File.WriteAllBytes(c, new byte[] { 3, 2, 1 });
            var groups = new List<string[]>(); var failures = new List<string>();
            foreach (var step in AssetContentDuplicates.Check(new[] { a, a, b, c, Path.Combine(_temp, "missing.bin") }, groups.Add, (p, m) => failures.Add(p))) { }
            Assert.That(groups.Count, Is.EqualTo(1)); Assert.That(groups[0], Is.EquivalentTo(new[] { a, b })); Assert.That(failures.Count, Is.EqualTo(1));
        }
        [Test] public void ImportedFileCanUseMultipleApplicableModulesWithoutLosingSubAssets()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, subMeshCount = 5 };
            for (var i = 0; i < 5; i++) mesh.SetTriangles(new[] { 0, 1, 2 }, i);
            var path = _root + "/compound.asset"; AssetDatabase.CreateAsset(mesh, path);
            var material = new Material(Shader.Find("Hidden/InternalErrorShader")); AssetDatabase.AddObjectToAsset(material, path); AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(path)) });
            using (var session = new BundleInspectionSession(BundleBuildReport.Read(_report), AssetCheckSettings.CreateDefault(), () => false))
            {
                Finish(session); var result = session.Resources.Single(); Assert.That(result.Issues.Any(i => i.RuleId == "model.mesh-submeshes"), Is.True);
                Assert.That(result.Issues.Any(i => i.RuleId == "material.missing-shader"), Is.True);
            }
        }
        [Test] public void SameGuidAcrossReportPathsIsOneCheckedIdentityWithAllOwners()
        {
            var guid = new string('a', 32);
            Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), new BundleReportAssetData { AssetPath = "Assets/old.png", AssetGUID = guid }),
                Bundle("B", 200, Array.Empty<string>(), new BundleReportAssetData { AssetPath = "Assets/renamed.png", AssetGUID = guid }) });
            using (var session = new BundleInspectionSession(BundleBuildReport.Read(_report), AssetCheckSettings.CreateDefault(), () => false))
            { Finish(session); Assert.That(session.Resources.Length, Is.EqualTo(1)); Assert.That(session.Resources[0].Asset.Owners.Count, Is.EqualTo(2)); }
        }
        [Test] public void DisposingContentInspectionReleasesOpenFileStream()
        {
            var a = Path.Combine(_temp, "large-a.bin"); var b = Path.Combine(_temp, "large-b.bin");
            File.WriteAllBytes(a, new byte[200000]); File.Copy(a, b);
            using (var scan = AssetContentDuplicates.Check(new[] { a, b }, _ => { }, (p, e) => Assert.Fail(e)).GetEnumerator())
            { Assert.That(scan.MoveNext(), Is.True); Assert.That(scan.MoveNext(), Is.True); Assert.That(scan.MoveNext(), Is.True); }
            using (File.Open(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        [Test] public void CancelAfterRulesBeforeContentComparisonDoesNotReportFullPass()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
            var path = _root + "/simple.asset"; AssetDatabase.CreateAsset(mesh, path);
            Write(_report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(path)) });
            using (var session = new BundleInspectionSession(BundleBuildReport.Read(_report), AssetCheckSettings.CreateDefault(), () => false))
            {
                for (var i = 0; !session.Resources[0].Checked && session.Running && i < 1000; i++) session.Step();
                Assert.That(session.Resources[0].Checked, Is.True); Assert.That(session.Resources[0].Issues, Is.Empty);
                session.Cancel(); Assert.That(session.Resources[0].ContentChecked, Is.False); Assert.That(session.Resources[0].Status, Is.EqualTo("未完成")); Assert.That(session.Complete, Is.False);
            }
        }
        [Test] public void VariantAtlasMipMapsFollowItsMaster()
        {
            var master = new SpriteAtlas(); var settings = master.GetTextureSettings(); settings.generateMipMaps = true; master.SetTextureSettings(settings);
            AssetDatabase.CreateAsset(master, _root + "/master.spriteatlas");
            var variant = new SpriteAtlas(); variant.SetIsVariant(true); variant.SetMasterAtlas(master); var path = _root + "/variant.spriteatlas"; AssetDatabase.CreateAsset(variant, path);
            Assert.That(new TextureCheckModule().Check(path, AssetCheckSettings.CreateDefault().CurrentStandards).Any(i => i.RuleId == "atlas.mipmaps"), Is.True);
            AssetDatabase.DeleteAsset(_root + "/master.spriteatlas");
            var issues = new List<AssetCheckIssue>();
            new TextureMipMapRule().Check(new TextureCheckContext(path, null, variant, new TextureStandards(), issues));
            Assert.That(issues.Single().Unverified, Is.True);
        }
    }
}
