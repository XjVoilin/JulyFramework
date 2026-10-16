using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class ExtendedAssetCheckTests
    {
        private string _root;
        private string _config;

        [SetUp]
        public void SetUp()
        {
            var id = Guid.NewGuid().ToString("N");
            _root = "Assets/__JulyAssetCheckExtended_" + id;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _config = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "AssetCheckExtended_" + id);
            Directory.CreateDirectory(_config);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_root);
            var allowed = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Temp")) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(_config), Does.StartWith(allowed));
            Directory.Delete(_config, true);
        }

        [Test]
        public void VersionOneMigrationPreservesExistingStandardsAndExclusions()
        {
            var legacy = AssetCheckSettings.CreateDefault();
            legacy.version = 1;
            legacy.CurrentStandards.texture.compressionFormat = TextureImporterFormat.ASTC_8x8;
            legacy.CurrentStandards.texture.maxTextureSize = 0;
            legacy.CurrentStandards.model.maxTrianglesPerMesh = 777;
            legacy.CurrentStandards.model.allowLights = true;
            legacy.ignoredAssets.Add(new IgnoredAsset { guid = new string('b', 32), kind = AssetCheckKind.Model });
            File.WriteAllText(Path.Combine(_config, AssetCheckSettingsFile.FileName), JsonUtility.ToJson(legacy));
            var result = new AssetCheckSettingsFile(_config).LoadOrCreate();
            Assert.That(result.version, Is.EqualTo(3));
            Assert.That(result.CurrentStandards.texture.compressionFormat, Is.EqualTo(TextureImporterFormat.ASTC_8x8));
            Assert.That(result.CurrentStandards.texture.maxTextureSize, Is.EqualTo(1024));
            Assert.That(result.CurrentStandards.model.maxTrianglesPerMesh, Is.EqualTo(777));
            Assert.That(result.CurrentStandards.model.allowLights, Is.True);
            Assert.That(result.ignoredAssets.Single().guid, Is.EqualTo(new string('b', 32)));
            Assert.That(new AssetCheckSettingsFile(_config).LoadOrCreate().version, Is.EqualTo(3));
        }

        [Test]
        public void SmallSourceDoesNotFailBecauseItsMaxSizeIsLarge()
        {
            var path = Texture("small.png", 64, 32);
            Assert.That(new TextureCheckModule().Check(path, new PlatformStandards())
                .Any(issue => issue.RuleId == "texture.imported-size"), Is.False);
        }

        [Test]
        public void TextureSizeFixPreservesFormatDefaultAndOtherPlatform()
        {
            var path = Texture("large.png", 2048, 8);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            TuanjieTextureSettings.WriteFormat(importer, "MiniGame", TextureImporterFormat.ASTC_8x8);
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 2048;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var defaults = importer.GetDefaultPlatformTextureSettings().maxTextureSize;
            var module = new TextureCheckModule();
            var standard = new PlatformStandards();
            var issue = module.Check(path, standard).Single(i => i.RuleId == "texture.imported-size");
            module.Fix(path, new[] { issue }, standard);
            Assert.That(module.Check(path, standard).Any(i => i.RuleId == issue.RuleId), Is.False);
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.GetPlatformTextureSettings("MiniGame").format, Is.EqualTo(TextureImporterFormat.ASTC_8x8));
            Assert.That(importer.GetPlatformTextureSettings("Android").maxTextureSize, Is.EqualTo(2048));
            Assert.That(importer.GetDefaultPlatformTextureSettings().maxTextureSize, Is.EqualTo(defaults));
            Assert.That(importer.GetPlatformTextureSettings("WeixinMiniGame").format, Is.EqualTo(TextureImporterFormat.Automatic));
        }

        [Test]
        public void ReadWritePermissionAllowsWithoutForcingAndFixOnlyClosesReadWrite()
        {
            var path = Texture("readable.png", 8, 8);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true;
            importer.SaveAndReimport();
            var standard = new PlatformStandards();
            var module = new TextureCheckModule();
            var issue = module.Check(path, standard).Single(i => i.RuleId == "texture.read-write");
            standard.texture.allowReadWrite = true;
            Assert.That(module.Check(path, standard).Any(i => i.RuleId == issue.RuleId), Is.False);
            standard.texture.allowReadWrite = false;
            module.Fix(path, new[] { issue }, standard);
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, Is.False);
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).GetPlatformTextureSettings("WebGL").overridden, Is.False);
        }

        [Test]
        public void AtlasFixesTargetsSizeAndReadabilityWithoutPackingAllAtlases()
        {
            var atlas = new SpriteAtlas();
            var path = _root + "/atlas.spriteatlas";
            AssetDatabase.CreateAsset(atlas, path);
            var defaults = atlas.GetPlatformSettings("DefaultTexturePlatform");
            defaults.maxTextureSize = 4096;
            atlas.SetPlatformSettings(defaults);
            var texture = atlas.GetTextureSettings();
            texture.readable = true;
            texture.generateMipMaps = true;
            atlas.SetTextureSettings(texture);
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            var module = new TextureCheckModule();
            var standards = new PlatformStandards();
            var issues = module.Check(path, standards);
            Assert.That(issues.Select(i => i.RuleId), Is.EquivalentTo(new[] { "atlas.compression-format", "atlas.page-size", "atlas.read-write" }));
            Assert.That(issues.Single(i => i.RuleId == "atlas.compression-format").Unverified, Is.True);
            module.Fix(path, issues, standards);
            Assert.That(module.Check(path, standards), Is.Empty);
            atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            Assert.That(atlas.GetTextureSettings().generateMipMaps, Is.True);
            Assert.That(atlas.GetPlatformSettings("DefaultTexturePlatform").maxTextureSize, Is.EqualTo(4096));
        }

        [Test]
        public void MaterialEmptyTextureSlotsPassButDeletedTextureReferencesFail()
        {
            var texturePath = Texture("deleted.png", 8, 8);
            var material = new Material(Shader.Find("Unlit/Texture"));
            var path = _root + "/material.mat";
            AssetDatabase.CreateAsset(material, path);
            var module = new MaterialCheckModule();
            Assert.That(module.Check(path, new PlatformStandards()), Is.Empty);
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(texturePath);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var issues = module.Check(path, new PlatformStandards());
            Assert.That(issues.Single().RuleId, Is.EqualTo("reference.missing"));
            Assert.That(issues.Single().CanFix, Is.False);
        }

        [Test]
        public void MaterialShaderLossIsNotAutomaticallyReplaced()
        {
            var shaderPath = _root + "/temporary.shader";
            File.WriteAllText(shaderPath, "Shader \"AssetCheck/Temporary\" { SubShader { Pass {} } }");
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath));
            var path = _root + "/shader-loss.mat";
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(shaderPath);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var issues = new MaterialCheckModule().Check(path, new PlatformStandards());
            Assert.That(issues.Any(i => i.RuleId == "material.missing-shader"), Is.True);
            Assert.That(issues.All(i => !i.CanFix), Is.True);
        }

        [Test]
        public void PrefabLostMeshIsReportedWithObjectComponentAndField()
        {
            var mesh = new Mesh();
            var meshPath = _root + "/mesh.asset";
            AssetDatabase.CreateAsset(mesh, meshPath);
            var root = new GameObject("Root");
            var path = _root + "/references.prefab";
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var issue = new PrefabSceneCheckModule().Check(path, new PlatformStandards()).Single();
            Assert.That(issue.RuleId, Is.EqualTo("reference.missing"));
            Assert.That(issue.ResourceName, Does.Contain("references[0]").And.Contain("MeshFilter").And.Contain("Mesh"));
        }

        [Test]
        public void DeletedNestedPrefabBelongsToReferenceRule()
        {
            var child = new GameObject("Child");
            var childPath = _root + "/child.prefab";
            var root = new GameObject("Parent");
            var path = _root + "/parent.prefab";
            try
            {
                var source = PrefabUtility.SaveAsPrefabAsset(child, childPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                instance.transform.SetParent(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(child); }
            AssetDatabase.DeleteAsset(childPath);
            LogAssert.Expect(LogType.Error, new Regex("Problem detected while importing the Prefab file:[\\s\\S]*Missing Nested Prefab Asset"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            LogAssert.Expect(LogType.Error, new Regex("Problem detected while importing the Prefab file:[\\s\\S]*Missing Nested Prefab Asset"));
            LogAssert.Expect(LogType.Error, new Regex("Problem detected while loading the contents of the Prefab file:"));
            LogAssert.Expect(LogType.Error, new Regex("^Nested Prefab problem. Missing Nested Prefab Asset:"));
            var issues = new PrefabSceneCheckModule().Check(path, new PlatformStandards());
            Assert.That(issues.Any(i => i.RuleId == "reference.missing" && i.Actual.Contains("Prefab")), Is.True);
        }

        [Test]
        public void InheritedBrokenReferenceIsNotRepeatedInParentPrefab()
        {
            var meshPath = _root + "/mesh.asset";
            AssetDatabase.CreateAsset(new Mesh(), meshPath);
            var child = new GameObject("Child");
            var root = new GameObject("Parent");
            var childPath = _root + "/source.prefab";
            var parentPath = _root + "/consumer.prefab";
            try
            {
                child.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                var source = PrefabUtility.SaveAsPrefabAsset(child, childPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                instance.transform.SetParent(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, parentPath);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(child); }
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.ImportAsset(childPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(parentPath, ImportAssetOptions.ForceSynchronousImport);
            var module = new PrefabSceneCheckModule();
            Assert.That(module.Check(childPath, new PlatformStandards()).Count, Is.EqualTo(1));
            Assert.That(module.Check(parentPath, new PlatformStandards()), Is.Empty);
        }

        [Test]
        public void MissingScriptIsLocatedWithoutDeletingTheComponent()
        {
            var path = _root + "/missing-script.prefab";
            var root = new GameObject("Root");
            try
            {
                root.AddComponent<MeshFilter>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
            var text = File.ReadAllText(path);
            var match = Regex.Match(text, @"--- !u!33 &(?<id>\d+)\r?\nMeshFilter:\r?\n(?<body>[\s\S]*?)(?=--- !u!|\z)");
            Assert.That(match.Success, Is.True);
            var owner = Regex.Match(match.Groups["body"].Value, @"m_GameObject: \{fileID: (?<id>\d+)\}").Groups["id"].Value;
            var replacement = "--- !u!114 &" + match.Groups["id"].Value + "\nMonoBehaviour:\n" +
                "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n" +
                "  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: " + owner + "}\n  m_Enabled: 1\n" +
                "  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n" +
                "  m_Name: \n  m_EditorClassIdentifier: \n";
            File.WriteAllText(path, text.Remove(match.Index, match.Length).Insert(match.Index, replacement));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var before = File.ReadAllBytes(path);
            var issue = new PrefabSceneCheckModule().Check(path, new PlatformStandards()).Single();
            Assert.That(issue.RuleId, Is.EqualTo("hierarchy.missing-script"));
            Assert.That(issue.ResourceName, Does.Contain("Component[1]"));
            Assert.That(issue.CanFix, Is.False);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void AllMaterialsInOneFileAreCheckedAndWholeFileIgnoreAlsoExcludesMeshRules()
        {
            var path = _root + "/combined.asset";
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one }, triangles = new[] { 0, 1, 2, 1, 3, 2 } };
            AssetDatabase.CreateAsset(mesh, path);
            for (var index = 0; index < 2; index++)
                AssetDatabase.AddObjectToAsset(new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Broken" + index }, path);
            AssetDatabase.SaveAssets();
            var issues = new MaterialCheckModule().Check(path, new PlatformStandards());
            Assert.That(issues.Count, Is.EqualTo(2));
            Assert.That(issues.Select(i => i.LocalId).Distinct().Count(), Is.EqualTo(2));
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config)))
            {
                workspace.ChangeSettings(s => { s.scope = _root; s.CurrentStandards.model.maxTrianglesPerMesh = 1; });
                workspace.StartScan(AssetCheckKind.Model);
                Drain(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Model).Issues.Count, Is.EqualTo(1));
                workspace.Ignore(issues[0]);
                Assert.That(workspace.GetReport(AssetCheckKind.Model).Issues, Is.Empty);
                workspace.StartScan(AssetCheckKind.Material);
                Drain(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Material).IgnoredTargets.Count, Is.EqualTo(1));
                Assert.That(workspace.GetReport(AssetCheckKind.Material).PassedAssets, Is.Zero);
            }
        }

        [TestCase("m_SupportsHDR", 1, "urp.hdr")]
        [TestCase("m_RequireDepthTexture", 1, "urp.depth-texture")]
        [TestCase("m_RequireOpaqueTexture", 1, "urp.opaque-texture")]
        [TestCase("m_MSAA", 4, "urp.msaa")]
        [TestCase("m_RenderScale", 1.5, "urp.render-scale")]
        [TestCase("m_UseSRPBatcher", 0, "urp.srp-batcher")]
        [TestCase("m_MainLightShadowsSupported", 1, "urp.main-shadows")]
        [TestCase("m_MainLightShadowmapResolution", 2048, "urp.main-shadow-resolution")]
        [TestCase("m_ShadowDistance", 100, "urp.shadow-distance")]
        [TestCase("m_ShadowCascadeCount", 4, "urp.shadow-cascades")]
        [TestCase("m_SoftShadowsSupported", 1, "urp.soft-shadows")]
        [TestCase("m_AdditionalLightsRenderingMode", 2, "urp.additional-lights")]
        [TestCase("m_AdditionalLightShadowsSupported", 1, "urp.additional-shadows")]
        public void EachUrpSettingCanBeFixedIndividuallyOnARealPipeline(string field, double value, string rule)
        {
            var path = CloneUrp();
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            int opaqueDownsampling;
            using (var serialized = new SerializedObject(asset))
            {
                opaqueDownsampling = serialized.FindProperty("m_OpaqueDownsampling").intValue;
                serialized.FindProperty("m_MainLightRenderingMode").intValue = 1;
                serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
                serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1;
                var property = serialized.FindProperty(field);
                if (property.propertyType == SerializedPropertyType.Boolean) property.boolValue = value != 0;
                else if (property.propertyType == SerializedPropertyType.Float) property.floatValue = (float)value;
                else property.intValue = (int)value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssetIfDirty(asset);
            var standards = new PlatformStandards();
            if (rule == "urp.main-shadows") standards.urp.allowMainLightShadows = false;
            var module = new UrpCheckModule();
            var issue = module.Check(path, standards).Single(i => i.RuleId == rule);
            module.Fix(path, new[] { issue }, standards);
            Assert.That(module.Check(path, standards).Any(i => i.RuleId == rule), Is.False);
            using (var serialized = new SerializedObject(asset))
                Assert.That(serialized.FindProperty("m_OpaqueDownsampling").intValue, Is.EqualTo(opaqueDownsampling));
        }

        [Test]
        public void SceneScanPreservesActiveDirtySceneAndDoesNotSave()
        {
            var original = SceneManager.GetActiveScene();
            var path = _root + "/scene.unity";
            WriteEmptyScene(path);
            SceneManager.SetActiveScene(original);
            var initialBytes = File.ReadAllBytes(path);
            var dirty = original.isDirty;
            Assert.That(new PrefabSceneCheckModule().Check(path, new PlatformStandards()), Is.Empty);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(original));
            Assert.That(original.isDirty, Is.EqualTo(dirty));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(initialBytes));
        }

        [Test]
        public void UnsavedLoadedSceneIsUnverifiedInsteadOfPassingOrBeingSaved()
        {
            var path = _root + "/dirty.unity";
            WriteEmptyScene(path);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var bytes = File.ReadAllBytes(path);
                EditorSceneManager.MarkSceneDirty(scene);
                var issue = new PrefabSceneCheckModule().Check(path, new PlatformStandards()).Single();
                Assert.That(issue.Unverified, Is.True);
                Assert.That(scene.isDirty, Is.True);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void AudioRulesReadImportedRateAndChannelsWithoutModifyingClip()
        {
            var path = _root + "/quad.wav";
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                const int channels = 4, rate = 48000, frames = 4800;
                var bytes = frames * channels * 2;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)channels); writer.Write(rate);
                writer.Write(rate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes); writer.Write(new byte[bytes]);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var before = File.ReadAllBytes(path + ".meta");
            var issues = new AudioCheckModule().Check(path, new PlatformStandards());
            Assert.That(issues.Select(i => i.RuleId), Is.EquivalentTo(new[] { "audio.sample-rate", "audio.channels" }));
            Assert.That(issues.All(i => !i.CanFix), Is.True);
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(before));
        }

        [Test]
        public void ProjectSettingsCanBeScannedAndIgnoredWithoutAssetGuidOrExistingResourceFolder()
        {
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config)))
            {
                workspace.ChangeSettings(s =>
                {
                    s.scope = "Assets/FolderThatDoesNotExist";
                    s.CurrentStandards.project.colorSpace = PlayerSettings.colorSpace == ColorSpace.Linear ? ColorSpace.Gamma : ColorSpace.Linear;
                });
                workspace.StartScan(AssetCheckKind.Project);
                Drain(workspace);
                var issue = workspace.GetReport(AssetCheckKind.Project).Issues.Single(i => i.RuleId == "project.color-space");
                workspace.Ignore(issue);
                Assert.That(workspace.Settings.ignoredAssets.Single().guid, Is.Null.Or.Empty);
                Assert.That(workspace.Settings.ignoredAssets.Single().projectPath, Is.EqualTo("ProjectSettings/ProjectSettings.asset"));
                workspace.StartScan(AssetCheckKind.Project);
                Drain(workspace);
                var report = workspace.GetReport(AssetCheckKind.Project);
                Assert.That(report.IgnoredTargets.Count, Is.EqualTo(1));
                Assert.That(report.CheckedTargets.Count, Is.EqualTo(2));
                Assert.That(report.Issues.Any(i => i.TargetId == issue.TargetId), Is.False);
                workspace.Restore(issue.TargetId);
                Assert.That(report.Stale, Is.True);
            }
        }

        [Test]
        public void ScopeChangeDoesNotInvalidateGlobalConfigurationReports()
        {
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config)))
            {
                workspace.StartScan(AssetCheckKind.Project);
                Drain(workspace);
                workspace.ChangeSettings(s => s.scope = _root);
                Assert.That(workspace.GetReport(AssetCheckKind.Project).Stale, Is.False);
            }
        }

        [Test]
        public void CatalogHasSevenTypesAndWindowBuildsTheirTabs()
        {
            Assert.That(AssetCheckCatalog.Entries.Select(e => e.Kind).Distinct().Count(), Is.EqualTo(7));
            var window = ScriptableObject.CreateInstance<AssetCheckWindow>();
            try
            {
                window.CreateGUI();
                Assert.That(window.rootVisualElement.Query<UnityEngine.UIElements.Button>(className: "type-tab").ToList().Count, Is.EqualTo(7));
                foreach (var entry in AssetCheckCatalog.Entries)
                    Assert.That(AssetCheckStandardsView.Build(entry.Kind, AssetCheckSettings.CreateDefault(), _ => { }), Is.Not.Null);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void UrpFixRespectsInactiveShadowSettingsAndDoesNotTouchRendererFeatures()
        {
            var path = CloneUrp();
            var pipeline = AssetDatabase.LoadMainAssetAtPath(path);
            using (var serialized = new SerializedObject(pipeline))
            {
                serialized.FindProperty("m_SupportsHDR").boolValue = true;
                serialized.FindProperty("m_RequireDepthTexture").boolValue = true;
                serialized.FindProperty("m_MainLightShadowsSupported").boolValue = false;
                serialized.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
                serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = 0;
                serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssetIfDirty(pipeline);
            var module = new UrpCheckModule();
            var standards = new PlatformStandards();
            var issues = module.Check(path, standards);
            Assert.That(issues.Any(i => i.RuleId == "urp.hdr"), Is.True);
            Assert.That(issues.Any(i => i.RuleId == "urp.depth-texture"), Is.True);
            Assert.That(issues.Any(i => i.RuleId == "urp.main-shadow-resolution" || i.RuleId == "urp.additional-shadows"), Is.False);
            module.Fix(path, issues.Where(i => i.CanFix).ToArray(), standards);
            Assert.That(module.Check(path, standards).Any(i => i.CanFix), Is.False);
            using (var serialized = new SerializedObject(pipeline))
            {
                Assert.That(serialized.FindProperty("m_MainLightShadowmapResolution").intValue, Is.EqualTo(4096));
                Assert.That(serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue, Is.True);
            }
        }

        [Test]
        public void UrpCollectorUsesProjectReferencesAndDeduplicatesRenderersOutsideScope()
        {
            var path = CloneUrp();
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
            var previous = GraphicsSettings.defaultRenderPipeline;
            try
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                var ids = new UrpCheckModule().Collect("Assets/UnrelatedFolder");
                Assert.That(ids, Does.Contain(AssetCheckTarget.Id(path)));
                Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length));
                using (var serialized = new SerializedObject(pipeline))
                {
                    var list = serialized.FindProperty("m_RendererDataList");
                    Assert.That(list.arraySize, Is.GreaterThan(0));
                    Assert.That(ids, Does.Contain(AssetCheckTarget.Id(AssetDatabase.GetAssetPath(list.GetArrayElementAtIndex(0).objectReferenceValue))));
                }
            }
            finally { GraphicsSettings.defaultRenderPipeline = previous; }
        }

        private string CloneUrp()
        {
            var source = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset")
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(path => !path.StartsWith(_root, StringComparison.Ordinal));
            if (source == null)
                Assert.Ignore("需要项目中存在真实 URP 资源；纯 Built-in 项目不运行 URP 集成测试。");
            var path = _root + "/pipeline.asset";
            var pipeline = Object.Instantiate(AssetDatabase.LoadMainAssetAtPath(source));
            AssetDatabase.CreateAsset(pipeline, path);
            var renderer = ScriptableObject.CreateInstance("UniversalRendererData");
            AssetDatabase.CreateAsset(renderer, _root + "/renderer.asset");
            using (var serialized = new SerializedObject(pipeline))
            {
                var list = serialized.FindProperty("m_RendererDataList");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return path;
        }

        private string Texture(string name, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var path = _root + "/" + name;
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        private static void WriteEmptyScene(string path)
        {
            File.WriteAllText(path, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!104 &1\nRenderSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 9\n  m_Fog: 0\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void Drain(AssetCheckWorkspace workspace)
        {
            for (var count = 0; workspace.Running && count < 2000; count++)
                workspace.Step();
            Assert.That(workspace.Running, Is.False);
            Assert.That(workspace.Failure, Is.Null);
        }
    }
}
