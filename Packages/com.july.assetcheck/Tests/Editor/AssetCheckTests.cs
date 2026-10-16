using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class AssetCheckTests
    {
        private string _root;
        private string _configDirectory;

        [SetUp]
        public void SetUp()
        {
            var id = Guid.NewGuid().ToString("N");
            _root = "Assets/__JulyAssetCheckTests_" + id;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _configDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "JulyAssetCheckTests_" + id);
            Directory.CreateDirectory(_configDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_root);
            var tempRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Temp")) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(_configDirectory), Does.StartWith(tempRoot));
            Directory.Delete(_configDirectory, true);
        }

        [Test]
        public void ConfigurationRoundTripsProjectStandardsAndWholeAssetExclusions()
        {
            var file = new AssetCheckSettingsFile(_configDirectory);
            var settings = file.LoadOrCreate();
            settings.scope = _root;
            settings.CurrentStandards.model.allowLights = true;
            settings.CurrentStandards.texture.compressionFormat = TextureImporterFormat.ASTC_8x8;
            settings.ignoredAssets.Add(new IgnoredAsset { guid = new string('a', 32), lastKnownPath = _root + "/cup.obj", kind = AssetCheckKind.Model });
            file.Save(settings);
            var loaded = new AssetCheckSettingsFile(_configDirectory).LoadOrCreate();
            Assert.That(loaded.CurrentStandards.model.allowLights, Is.True);
            Assert.That(loaded.CurrentStandards.texture.compressionFormat, Is.EqualTo(TextureImporterFormat.ASTC_8x8));
            Assert.That(loaded.ignoredAssets.Single().guid, Is.EqualTo(new string('a', 32)));
        }

        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("<<<<<<< HEAD\n{}\n=======\n{}\n>>>>>>> branch")]
        public void InvalidConfigurationIsPreserved(string invalid)
        {
            var path = Path.Combine(_configDirectory, AssetCheckSettingsFile.FileName);
            File.WriteAllText(path, invalid);
            Assert.Throws<InvalidDataException>(() => new AssetCheckSettingsFile(_configDirectory).LoadOrCreate());
            Assert.That(File.ReadAllText(path), Is.EqualTo(invalid));
        }

        [Test]
        public void ExternalConfigurationEditCannotBeOverwritten()
        {
            var file = new AssetCheckSettingsFile(_configDirectory);
            var settings = file.LoadOrCreate();
            var path = Path.Combine(_configDirectory, AssetCheckSettingsFile.FileName);
            File.WriteAllText(path, "external edit");
            Assert.Throws<IOException>(() => file.Save(settings));
            Assert.That(File.ReadAllText(path), Is.EqualTo("external edit"));
        }

        [TestCase("Assets/../Packages")]
        [TestCase("AssetsOther")]
        [TestCase("Assets//Models")]
        [TestCase("Packages")]
        public void ScopeCannotEscapeAssets(string path)
        {
            Assert.Throws<InvalidDataException>(() => AssetCheckSettings.ValidateScope(path));
        }

        [Test]
        public void TextureFixChangesOnlyFailingTargetsAndPreservesOtherSettings()
        {
            var path = CreateTexture("texture.png");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            foreach (var target in MiniGameProfile.TextureTargets)
                SetTextureFormat(importer, target, TextureImporterFormat.RGBA32, 256);
            SetTextureFormat(importer, "WebGL", TextureImporterFormat.ASTC_6x6, 128);
            SetTextureFormat(importer, "Android", TextureImporterFormat.ASTC_4x4, 512);
            importer.SaveAndReimport();
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var defaults = importer.GetDefaultPlatformTextureSettings();
            var standards = new PlatformStandards();
            var module = new TextureCheckModule();
            var issue = module.Check(path, standards).Single();
            Assert.That(issue.Targets, Is.EquivalentTo(new[] { "MiniGame", "WeixinMiniGame" }));
            module.Fix(path, new[] { issue }, standards);
            Assert.That(module.Check(path, standards), Is.Empty);
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.GetPlatformTextureSettings("WebGL").maxTextureSize, Is.EqualTo(128));
            Assert.That(importer.GetPlatformTextureSettings("MiniGame").maxTextureSize, Is.EqualTo(256));
            Assert.That(importer.GetPlatformTextureSettings("Android").format, Is.EqualTo(TextureImporterFormat.ASTC_4x4));
            var after = importer.GetDefaultPlatformTextureSettings();
            Assert.That(after.format, Is.EqualTo(defaults.format));
            Assert.That(after.maxTextureSize, Is.EqualTo(defaults.maxTextureSize));
            Assert.That(after.textureCompression, Is.EqualTo(defaults.textureCompression));
        }

        [Test]
        public void MissingMiniGameSettingsAreConfirmedAndCanBeConfigured()
        {
            var path = CreateTexture("automatic.png");
            var standards = new PlatformStandards();
            var module = new TextureCheckModule();
            var issue = module.Check(path, standards).Single();
            Assert.That(issue.Targets, Does.Contain("MiniGame"));
            Assert.That(issue.HasProblem, Is.True);
            Assert.That(issue.UnverifiedTargets, Does.Not.Contain("MiniGame"));
            Assert.That(issue.ConfirmedFix().Targets, Does.Contain("MiniGame"));
            module.Fix(path, new[] { issue }, standards);
            Assert.That(module.Check(path, standards), Is.Empty);
        }

        [Test]
        public void CreatingPlatformOverridesPreservesInheritedSizeAndQuality()
        {
            var path = CreateTexture("inherited.png");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var defaults = importer.GetDefaultPlatformTextureSettings();
            defaults.maxTextureSize = 128;
            defaults.compressionQuality = 73;
            defaults.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(defaults);
            importer.SaveAndReimport();
            var module = new TextureCheckModule();
            var standards = new PlatformStandards();
            module.Fix(path, module.Check(path, standards), standards);
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            foreach (var target in MiniGameProfile.TextureTargets)
            {
                var settings = importer.GetPlatformTextureSettings(target);
                Assert.That(settings.maxTextureSize, Is.EqualTo(128), target);
                Assert.That(settings.compressionQuality, Is.EqualTo(73), target);
                Assert.That(settings.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6), target);
            }
            Assert.That(importer.GetDefaultPlatformTextureSettings().textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
        }

        [Test]
        public void ModelRulesApplyPermissionsWithoutRequiringEveryModelToEnableAllowedOptions()
        {
            var path = CreateModel();
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importLights = true;
            importer.importCameras = true;
            importer.isReadable = true;
            importer.addCollider = true;
            importer.SaveAndReimport();
            var module = new ModelCheckModule();
            var standards = new PlatformStandards();
            standards.model.maxTrianglesPerMesh = 1;
            Assert.That(module.Check(path, standards).Count, Is.EqualTo(5));
            standards.model.allowLights = true;
            Assert.That(module.Check(path, standards).Any(issue => issue.RuleId == "model.import-lights"), Is.False);
            importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importLights = false;
            importer.SaveAndReimport();
            Assert.That(module.Check(path, standards).Any(issue => issue.RuleId == "model.import-lights"), Is.False);
        }

        [Test]
        public void ModelFixKeepsAllowedLightsAndMeshChecksWorkWithoutReadWrite()
        {
            var path = CreateModel();
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importLights = true;
            importer.isReadable = true;
            importer.SaveAndReimport();
            var standards = new PlatformStandards();
            standards.model.allowLights = true;
            standards.model.maxTrianglesPerMesh = 1;
            var module = new ModelCheckModule();
            var fixable = module.Check(path, standards).Where(issue => issue.CanFix).ToArray();
            module.Fix(path, fixable, standards);
            importer = (ModelImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.importLights, Is.True);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(module.Check(path, standards).Single().RuleId, Is.EqualTo("model.mesh-triangles"));
        }

        [Test]
        public void StandaloneMeshIsCollectedAndDoesNotReceiveImporterRules()
        {
            var mesh = new Mesh { name = "Standalone" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            var path = _root + "/standalone.asset";
            AssetDatabase.CreateAsset(mesh, path);
            var module = new ModelCheckModule();
            var standards = new PlatformStandards();
            standards.model.maxTrianglesPerMesh = 1;
            Assert.That(module.Collect(_root), Does.Contain(AssetDatabase.AssetPathToGUID(path)));
            Assert.That(module.Check(path, standards).Single().RuleId, Is.EqualTo("model.mesh-triangles"));
        }

        [Test]
        public void IgnoredModelSkipsEveryRuleAndRemainsIgnoredAfterMove()
        {
            var path = CreateModel();
            using (var workspace = CreateWorkspace())
            {
                workspace.ChangeSettings(settings => settings.CurrentStandards.model.maxTrianglesPerMesh = 1);
                workspace.StartScan(AssetCheckKind.Model);
                Drain(workspace);
                var issue = workspace.GetReport(AssetCheckKind.Model).Issues.First();
                var guid = issue.TargetId;
                workspace.Ignore(issue);
                Assert.That(workspace.GetReport(AssetCheckKind.Model).Issues, Is.Empty);
                var moved = _root + "/renamed.obj";
                Assert.That(AssetDatabase.MoveAsset(path, moved), Is.Empty);
                workspace.StartScan();
                Drain(workspace);
                var report = workspace.GetReport(AssetCheckKind.Model);
                Assert.That(report.CheckedTargets, Is.Empty);
                Assert.That(report.IgnoredTargets, Is.EquivalentTo(new[] { guid }));
                Assert.That(report.Issues, Is.Empty);
                workspace.Restore(guid);
                Assert.That(report.Stale, Is.True);
                workspace.StartScan(AssetCheckKind.Model);
                Drain(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Model).Issues, Is.Not.Empty);
            }
        }

        [Test]
        public void IgnoringOneMeshExcludesEveryMeshInTheSameFile()
        {
            var path = _root + "/multiple.asset";
            for (var index = 0; index < 2; index++)
            {
                var mesh = new Mesh { name = "Part" + index };
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
                mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
                if (index == 0)
                    AssetDatabase.CreateAsset(mesh, path);
                else
                    AssetDatabase.AddObjectToAsset(mesh, path);
            }
            AssetDatabase.SaveAssets();
            using (var workspace = CreateWorkspace())
            {
                workspace.ChangeSettings(settings => settings.CurrentStandards.model.maxTrianglesPerMesh = 1);
                workspace.StartScan(AssetCheckKind.Model);
                Drain(workspace);
                var report = workspace.GetReport(AssetCheckKind.Model);
                Assert.That(report.Issues.Count, Is.EqualTo(2));
                Assert.That(report.Issues.Select(issue => issue.LocalId).Distinct().Count(), Is.EqualTo(2));
                workspace.Ignore(report.Issues.First());
                Assert.That(report.Issues, Is.Empty);
                workspace.StartScan(AssetCheckKind.Model);
                Drain(workspace);
                report = workspace.GetReport(AssetCheckKind.Model);
                Assert.That(report.CheckedTargets, Is.Empty);
                Assert.That(report.IgnoredTargets.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void OnlyAffectedTypeBecomesStaleAndCancelledScanIsNotComplete()
        {
            CreateTexture("test.png");
            CreateModel();
            using (var workspace = CreateWorkspace())
            {
                workspace.StartScan();
                Drain(workspace);
                workspace.ChangeSettings(settings => settings.CurrentStandards.model.allowLights = true);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.False);
                Assert.That(workspace.GetReport(AssetCheckKind.Model).Stale, Is.True);
                Assert.Throws<InvalidOperationException>(() => workspace.StartFix(AssetCheckKind.Model,
                    workspace.GetReport(AssetCheckKind.Model).Issues));
                workspace.StartScan(AssetCheckKind.Texture);
                workspace.Step();
                workspace.Cancel();
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Complete, Is.False);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Cancelled, Is.True);
            }
        }

        [Test]
        public void BulkFixExcludesIgnoredResourcesAndRechecksTheResult()
        {
            var ignoredPath = CreateTexture("ignored.png");
            var fixedPath = CreateTexture("fixed.png");
            using (var workspace = CreateWorkspace())
            {
                workspace.StartScan(AssetCheckKind.Texture);
                Drain(workspace);
                var selection = workspace.GetReport(AssetCheckKind.Texture).Issues.ToArray();
                workspace.Ignore(selection.Single(issue => issue.AssetPath == ignoredPath));
                workspace.StartFix(AssetCheckKind.Texture, selection);
                Drain(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Issues, Is.Empty);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).IgnoredTargets.Count, Is.EqualTo(1));
                Assert.That(((TextureImporter)AssetImporter.GetAtPath(ignoredPath)).GetPlatformTextureSettings("MiniGame").overridden, Is.False);
                Assert.That(((TextureImporter)AssetImporter.GetAtPath(fixedPath)).GetPlatformTextureSettings("MiniGame").format,
                    Is.EqualTo(TextureImporterFormat.ASTC_6x6));
            }
        }

        [Test]
        public void WindowCreatesItsToolkitLayout()
        {
            var window = ScriptableObject.CreateInstance<AssetCheckWindow>();
            try
            {
                window.CreateGUI();
                Assert.That(window.rootVisualElement.Q<MultiColumnListView>(), Is.Not.Null);
                Assert.That(window.rootVisualElement.Query<Foldout>().ToList().Count, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        private AssetCheckWorkspace CreateWorkspace()
        {
            var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_configDirectory));
            workspace.ChangeSettings(settings => settings.scope = _root);
            return workspace;
        }

        private static void Drain(AssetCheckWorkspace workspace)
        {
            for (var step = 0; workspace.Running && step < 1000; step++)
                workspace.Step();
            Assert.That(workspace.Running, Is.False, "检查未结束");
            Assert.That(workspace.Failure, Is.Null);
        }

        private string CreateTexture(string fileName)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            try
            {
                var path = _root + "/" + fileName;
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                return path;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private string CreateModel()
        {
            var path = _root + "/cup.obj";
            File.WriteAllText(path, "o Cup\nv 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\nf 1 2 3\nf 2 4 3\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        private static void SetTextureFormat(TextureImporter importer, string target, TextureImporterFormat format, int maxSize)
        {
            var settings = importer.GetPlatformTextureSettings(target);
            settings.name = target;
            settings.overridden = true;
            settings.format = format;
            settings.maxTextureSize = maxSize;
            settings.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(settings);
        }
    }
}
