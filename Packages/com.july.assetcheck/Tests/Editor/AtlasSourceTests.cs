using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class AtlasSourceTests
    {
        private string _root, _config;

        [SetUp]
        public void Setup()
        {
            var id = Guid.NewGuid().ToString("N");
            _root = "Assets/__AtlasSource_" + id;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            AssetDatabase.CreateFolder(_root, "Sprites");
            AssetDatabase.CreateFolder(_root, "SpritesOther");
            _config = Path.GetFullPath(Path.Combine("Temp", "AtlasSource_" + id));
            Directory.CreateDirectory(_config);
        }

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(_root);
            Assert.That(_config, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar));
            Directory.Delete(_config, true);
        }

        [Test]
        public void DefaultAndLegacyConfigurationEnableOptionAndExplicitFalsePersists()
        {
            var settings = AssetCheckSettings.CreateDefault();
            Assert.That(settings.CurrentStandards.texture.requireUncompressedAtlasSources, Is.True);
            settings.version = 3;
            settings.CurrentStandards.model.maxTrianglesPerMesh = 777;
            var json = JsonUtility.ToJson(settings).Replace("\"requireUncompressedAtlasSources\":true,", "");
            var path = Path.Combine(_config, AssetCheckSettingsFile.FileName);
            File.WriteAllText(path, json);
            var file = new AssetCheckSettingsFile(_config);
            Assert.That(file.ReadExistingOrDefault().CurrentStandards.texture.requireUncompressedAtlasSources, Is.True);
            Assert.That(File.ReadAllText(path), Is.EqualTo(json), "只读入口不迁移磁盘配置");
            settings = file.LoadOrCreate();
            Assert.That(settings.version, Is.EqualTo(4));
            Assert.That(settings.CurrentStandards.model.maxTrianglesPerMesh, Is.EqualTo(777));
            settings.CurrentStandards.texture.requireUncompressedAtlasSources = false;
            file.Save(settings);
            Assert.That(new AssetCheckSettingsFile(_config).LoadOrCreate().CurrentStandards.texture.requireUncompressedAtlasSources, Is.False);
        }

        [Test]
        public void CheckboxUpdatesConfiguration()
        {
            var settings = AssetCheckSettings.CreateDefault();
            var view = AssetCheckStandardsView.Build(AssetCheckKind.Texture, settings, change => change(settings));
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                window.Show();
                window.rootVisualElement.Add(view);
                var toggle = view.Query<Toggle>().ToList().Single(field => field.label == "图集源图使用无压缩");
                Assert.That(toggle.value, Is.True);
                toggle.value = false;
                Assert.That(settings.CurrentStandards.texture.requireUncompressedAtlasSources, Is.False);
            }
            finally { window.Close(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitTextureOrSpritePackableProducesOnlySourceFormatProblem(bool spritePackable)
        {
            var path = Texture("Sprites/source.png");
            var packable = spritePackable ? (Object)AssetDatabase.LoadAssetAtPath<Sprite>(path) : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var atlasPath = Atlas(packable);
            var sourceBefore = File.ReadAllBytes(path + ".meta");
            var atlasBefore = File.ReadAllBytes(atlasPath);
            var module = new TextureCheckModule();
            module.Collect(_root + "/Sprites");
            var issues = module.Check(path, new PlatformStandards());
            Assert.That(issues.Select(issue => issue.RuleId), Is.EqualTo(new[] { "texture.atlas-source-format" }));
            Assert.That(issues.Single().Targets, Does.Contain("DefaultTexturePlatform"));
            CollectionAssert.AreEqual(sourceBefore, File.ReadAllBytes(path + ".meta"));
            CollectionAssert.AreEqual(atlasBefore, File.ReadAllBytes(atlasPath));
        }

        [Test]
        public void FolderMembershipIsRecursiveAndDoesNotMatchSiblingOrNonSprite()
        {
            AssetDatabase.CreateFolder(_root + "/Sprites", "Nested");
            var source = Texture("Sprites/Nested/source.png");
            var sibling = Texture("SpritesOther/source.png");
            var plain = Texture("Sprites/plain.png", false);
            Atlas(AssetDatabase.LoadAssetAtPath<DefaultAsset>(_root + "/Sprites"));
            var module = new TextureCheckModule();
            var standards = new PlatformStandards();
            Assert.That(module.Check(source, standards).Any(i => i.RuleId == "texture.atlas-source-format"), Is.True);
            Assert.That(module.Check(sibling, standards).Any(i => i.RuleId == "texture.compression-format"), Is.True);
            Assert.That(module.Check(plain, standards).Any(i => i.RuleId == "texture.compression-format"), Is.True);
        }

        [Test]
        public void DisabledOptionUsesOriginalFormatRuleAndAtlasStillUsesAtlasRules()
        {
            var source = Texture("Sprites/source.png");
            var atlas = Atlas(AssetDatabase.LoadAssetAtPath<Texture2D>(source));
            var standards = new PlatformStandards();
            standards.texture.requireUncompressedAtlasSources = false;
            var module = new TextureCheckModule();
            Assert.That(module.Check(source, standards).Any(i => i.RuleId == "texture.compression-format"), Is.True);
            Assert.That(module.Check(source, standards).Any(i => i.RuleId == "texture.atlas-source-format"), Is.False);
            standards.texture.requireUncompressedAtlasSources = true;
            Assert.That(module.Check(atlas, standards).Any(i => i.RuleId == "atlas.compression-format"), Is.True);
        }

        [Test]
        public void FixPreservesSizeReadabilityAndOtherPlatformsAndRecheckPasses()
        {
            var source = Texture("Sprites/source.png");
            Atlas(AssetDatabase.LoadAssetAtPath<Texture2D>(source));
            var importer = (TextureImporter)AssetImporter.GetAtPath(source);
            importer.isReadable = true;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.format = TextureImporterFormat.ASTC_4x4;
            android.maxTextureSize = 512;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            var standards = new PlatformStandards();
            var module = new TextureCheckModule();
            var issues = module.Check(source, standards);
            Assert.That(issues.Any(i => i.RuleId == "texture.read-write"), Is.True);
            module.Fix(source, issues.Where(i => i.RuleId == "texture.atlas-source-format").ToArray(), standards);
            Assert.That(module.Check(source, standards).Select(i => i.RuleId), Is.EqualTo(new[] { "texture.read-write" }));
            importer = (TextureImporter)AssetImporter.GetAtPath(source);
            Assert.That(importer.isReadable, Is.True);
            Assert.That(importer.GetDefaultPlatformTextureSettings().maxTextureSize, Is.EqualTo(256));
            Assert.That(importer.GetPlatformTextureSettings("Android").format, Is.EqualTo(TextureImporterFormat.ASTC_4x4));
            Assert.That(importer.GetPlatformTextureSettings("Android").maxTextureSize, Is.EqualTo(512));
        }

        [Test]
        public void InheritedUncompressedDoesNotRequireMiniGameOverridesAndExplicitCompressedOverrideFails()
        {
            var source = Texture("Sprites/source.png");
            Atlas(AssetDatabase.LoadAssetAtPath<Texture2D>(source));
            var importer = (TextureImporter)AssetImporter.GetAtPath(source);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var module = new TextureCheckModule();
            var standards = new PlatformStandards();
            Assert.That(module.Check(source, standards), Is.Empty);
            TuanjieTextureSettings.WriteFormat(importer, "MiniGame", TextureImporterFormat.ASTC_6x6);
            importer.SaveAndReimport();
            var issue = module.Check(source, standards).Single();
            Assert.That(issue.Targets, Is.EqualTo(new[] { "MiniGame" }));
            module.Fix(source, new[] { issue }, standards);
            Assert.That(module.Check(source, standards), Is.Empty);
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(source)).GetPlatformTextureSettings("WeixinMiniGame").overridden, Is.False);
        }

        [Test]
        public void AtlasChangesOutsideScopeInvalidateReportAndMembership()
        {
            var source = Texture("Sprites/source.png");
            var file = new AssetCheckSettingsFile(_config);
            using (var workspace = new AssetCheckWorkspace(file))
            {
                workspace.ChangeSettings(s => s.scope = _root + "/Sprites");
                workspace.StartScan(AssetCheckKind.Texture);
                Run(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.False);
                var atlas = Atlas(AssetDatabase.LoadAssetAtPath<Texture2D>(source));
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
                workspace.StartScan(AssetCheckKind.Texture);
                Run(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Issues.Single().RuleId, Is.EqualTo("texture.atlas-source-format"));
                workspace.StartFix(AssetCheckKind.Texture, workspace.GetReport(AssetCheckKind.Texture).Issues.ToArray());
                Run(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Issues, Is.Empty);
                AssetDatabase.DeleteAsset(atlas);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
                workspace.StartScan(AssetCheckKind.Texture);
                Run(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Issues.Single().RuleId, Is.EqualTo("texture.compression-format"));
            }
        }

        private static void Run(AssetCheckWorkspace workspace)
        {
            for (var step = 0; workspace.Running && step < 10000; step++) workspace.Step();
            Assert.That(workspace.Running, Is.False);
            Assert.That(workspace.Failure, Is.Null);
        }

        private string Texture(string name, bool sprite = true)
        {
            var path = _root + "/" + name;
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return path;
        }

        private string Atlas(Object packable)
        {
            var atlas = new SpriteAtlas();
            atlas.Add(new[] { packable });
            var path = _root + "/Source.spriteatlas";
            AssetDatabase.CreateAsset(atlas, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }
    }
}
