using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class ReviewRegressionTests
    {
        private string _root;
        private string _config;

        [SetUp]
        public void Setup()
        {
            var id = Guid.NewGuid().ToString("N");
            _root = "Assets/__AssetCheckReview_" + id;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _config = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "Review_" + id);
            Directory.CreateDirectory(_config);
        }

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(_root);
            Assert.That(Path.GetFullPath(_config), Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar));
            Directory.Delete(_config, true);
        }

        [Test]
        public void PerVertexStandardMustAcceptUrpPerVertexInsteadOfRequestingPerPixel()
        {
            var source = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            if (source == null) Assert.Ignore("需要真实 URP 资源运行此集成用例。");
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
                // URP 14 的 LightRenderingMode 明确定义 PerVertex=2、PerPixel=1。
                serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = 2;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var standards = new PlatformStandards();
            standards.urp.additionalLights = AdditionalLightStandard.PerVertex;
            var issue = new UrpCheckModule().Check(path, standards).SingleOrDefault(i => i.RuleId == "urp.additional-lights");
            Assert.That(issue, Is.Null, "A real PerVertex pipeline must pass the PerVertex project standard.");
        }

        [Test]
        public void RestoredResourceMustRemainStaleAfterEditingAnUnrelatedStandard()
        {
            var path = Texture();
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config)))
            {
                workspace.ChangeSettings(s => s.scope = _root);
                workspace.StartScan(AssetCheckKind.Texture);
                Drain(workspace);
                workspace.Ignore(workspace.GetReport(AssetCheckKind.Texture).Issues.First());
                workspace.Restore(AssetDatabase.AssetPathToGUID(path));
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
                workspace.ChangeSettings(s => s.CurrentStandards.model.allowLights = true);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True,
                    "An unrelated standard edit must not make an unscanned restored asset look fresh.");
            }
        }

        [Test]
        public void KnownFormatFailureMustRemainVisibleWhenOtherTargetsAreUnverified()
        {
            var path = Texture();
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            foreach (var target in new[] { "MiniGame", "WeixinMiniGame" })
            {
                var settings = importer.GetPlatformTextureSettings(target);
                settings.overridden = true;
                settings.format = TextureImporterFormat.Automatic;
                importer.SetPlatformTextureSettings(settings);
            }
            importer.SaveAndReimport();
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config)))
            {
                workspace.ChangeSettings(s => s.scope = _root);
                workspace.StartScan(AssetCheckKind.Texture);
                Drain(workspace);
                var report = workspace.GetReport(AssetCheckKind.Texture);
                var issue = report.Issues.Single();
                Assert.That(issue.Targets, Does.Contain("WebGL"));
                Assert.That(issue.Unverified, Is.True);
                Assert.That(report.ProblemAssets, Is.EqualTo(1),
                    "A known WebGL format mismatch must not disappear from the problem count.");
            }
        }

        [Test]
        public void ReplacingTabResultsMustNotCarrySelectionToDifferentAssets()
        {
            var model = new AssetCheckResultsModel();
            var before = new AssetCheckReport(AssetCheckKind.Texture, "first", AssetCheckPlatform.MiniGame, "Assets");
            before.Issues.Add(Issue("old"));
            model.Bind(before);
            model.Selected.Add(before.Issues[0].TargetId);
            var after = new AssetCheckReport(AssetCheckKind.Model, "second", AssetCheckPlatform.MiniGame, "Assets");
            after.Issues.Add(Issue("different"));
            model.Bind(after);
            Assert.That(model.Selected, Is.Empty, "Selections must belong to the report that was displayed.");
        }

        [Test]
        public void PerVertexForwardLightsMustNotReportInactiveShadowSettings()
        {
            var source = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            if (source == null) Assert.Ignore("需要真实 URP 资源运行此集成用例。");
            var path = _root + "/vertex-pipeline.asset";
            var pipeline = Object.Instantiate(AssetDatabase.LoadMainAssetAtPath(source));
            AssetDatabase.CreateAsset(pipeline, path);
            var renderer = ScriptableObject.CreateInstance("UniversalRendererData");
            Assert.That(renderer, Is.Not.Null);
            AssetDatabase.CreateAsset(renderer, _root + "/forward-renderer.asset");
            using (var serialized = new SerializedObject(renderer))
            {
                serialized.FindProperty("m_RenderingMode").intValue = 0; // 使用前向渲染，而非 Forward+。
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            using (var serialized = new SerializedObject(pipeline))
            {
                var list = serialized.FindProperty("m_RendererDataList");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
                serialized.FindProperty("m_MainLightShadowsSupported").boolValue = false;
                serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = 2; // URP 原生的逐顶点光照模式。
                serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
                serialized.FindProperty("m_ShadowDistance").floatValue = 100;
                serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var standards = new PlatformStandards();
            standards.urp.additionalLights = AdditionalLightStandard.PerVertex;
            var issues = new UrpCheckModule().Check(path, standards);
            Assert.That(issues.Any(i => i.RuleId == "urp.additional-shadows" || i.RuleId == "urp.shadow-distance" || i.RuleId == "urp.soft-shadows"),
                Is.False, "PerVertex Forward lights do not render additional-light shadows; the stored shadow options are inactive.");
        }

        [TestCase(AdditionalLightStandard.Disabled, 0)]
        [TestCase(AdditionalLightStandard.PerVertex, 2)]
        [TestCase(AdditionalLightStandard.PerPixel, 1)]
        public void LightModeFixWritesNativeEncodingAndPreservesJsonMeaning(AdditionalLightStandard standard, int native)
        {
            var pipeline = Pipeline(0);
            using (var serialized = new SerializedObject(pipeline))
            {
                serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = native == 0 ? 1 : 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var settings = AssetCheckSettings.CreateDefault();
            settings.CurrentStandards.urp.additionalLights = standard;
            var copy = settings.Copy();
            Assert.That(copy.CurrentStandards.urp.additionalLights, Is.EqualTo(standard));
            var module = new UrpCheckModule();
            var path = AssetDatabase.GetAssetPath(pipeline);
            var issue = module.Check(path, copy.CurrentStandards).Single(i => i.RuleId == "urp.additional-lights");
            module.Fix(path, new[] { issue }, copy.CurrentStandards);
            using (var serialized = new SerializedObject(pipeline))
                Assert.That(serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue, Is.EqualTo(native));
            Assert.That(module.Check(path, copy.CurrentStandards).Any(i => i.RuleId == "urp.additional-lights"), Is.False);
        }

        [Test]
        public void Renderer2DDoesNotReportUnused3DLightOrShadowOptions()
        {
            var pipeline = Pipeline(0, true);
            using (var serialized = new SerializedObject(pipeline))
            {
                serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
                serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1;
                serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
                serialized.FindProperty("m_ShadowDistance").floatValue = 100;
                serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var issues = new UrpCheckModule().Check(AssetDatabase.GetAssetPath(pipeline), new PlatformStandards());
            Assert.That(issues.Any(i => i.RuleId.Contains("shadow") || i.RuleId == "urp.additional-lights"), Is.False);
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        public void ForwardPlusReportsForcedPixelLightsWithoutOfferingAnIneffectiveFix(int nativeMode, bool shadows)
        {
            var pipeline = Pipeline(2);
            using (var serialized = new SerializedObject(pipeline))
            {
                serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = nativeMode;
                serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var issues = new UrpCheckModule().Check(AssetDatabase.GetAssetPath(pipeline), new PlatformStandards());
            Assert.That(issues.Single(i => i.RuleId == "urp.forward-plus-lights").CanFix, Is.False);
            Assert.That(issues.Any(i => i.RuleId == "urp.additional-shadows"), Is.EqualTo(shadows));
        }

        [Test]
        public void BulkFixDisablesLightsBeforeConsideringTheirDependentShadowBudgets()
        {
            var pipeline = Pipeline(0);
            using (var serialized = new SerializedObject(pipeline))
            {
                serialized.FindProperty("m_MainLightShadowsSupported").boolValue = false;
                serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1;
                serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
                serialized.FindProperty("m_ShadowDistance").floatValue = 100;
                serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var module = new UrpCheckModule();
            var path = AssetDatabase.GetAssetPath(pipeline);
            var standards = new PlatformStandards();
            module.Fix(path, module.Check(path, standards).Where(i => i.CanFix).ToArray(), standards);
            using (var serialized = new SerializedObject(pipeline))
            {
                Assert.That(serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue, Is.Zero);
                Assert.That(serialized.FindProperty("m_ShadowDistance").floatValue, Is.EqualTo(100));
                Assert.That(serialized.FindProperty("m_SoftShadowsSupported").boolValue, Is.True);
            }
            Assert.That(module.Check(path, standards).Any(i => i.CanFix), Is.False);
        }

        [Test]
        public void ReimportAndDeleteInvalidateCompletedReports()
        {
            var path = Texture();
            using (var workspace = Workspace(AssetCheckKind.Texture))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
                Assert.Throws<InvalidOperationException>(() => workspace.StartFix(AssetCheckKind.Texture,
                    workspace.GetReport(AssetCheckKind.Texture).Issues));
                workspace.StartScan(AssetCheckKind.Texture);
                Drain(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.False);
                AssetDatabase.DeleteAsset(path);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
            }
        }

        [Test]
        public void ChangedDependencyOutsideTheScopeInvalidatesItsConsumerReport()
        {
            var texture = Texture();
            AssetDatabase.CreateFolder(_root, "Materials");
            var material = new Material(Shader.Find("Unlit/Texture")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texture) };
            AssetDatabase.CreateAsset(material, _root + "/Materials/consumer.mat");
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config)))
            {
                workspace.ChangeSettings(s => s.scope = _root + "/Materials");
                workspace.StartScan(AssetCheckKind.Material);
                Drain(workspace);
                Assert.That(workspace.GetReport(AssetCheckKind.Material).Stale, Is.False);
                AssetDatabase.DeleteAsset(texture);
                Assert.That(workspace.GetReport(AssetCheckKind.Material).Stale, Is.True);
            }
        }

        [Test]
        public void ExternalConfigurationEditInvalidatesReportsAndCannotBeOverwritten()
        {
            Texture();
            using (var workspace = Workspace(AssetCheckKind.Texture))
            {
                var path = Path.Combine(_config, AssetCheckSettingsFile.FileName);
                File.AppendAllText(path, " ");
                workspace.PollExternalChanges();
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
                Assert.Throws<IOException>(() => workspace.ChangeSettings(s => s.CurrentStandards.model.allowLights = true));
                Assert.That(File.ReadAllText(path), Does.EndWith(" "));
            }
        }

        [Test]
        public void NativeProjectSettingChangeInvalidatesProjectReport()
        {
            using (var workspace = Workspace(AssetCheckKind.Project))
            {
                var path = "ProjectSettings/GraphicsSettings.asset";
                var stamp = File.GetLastWriteTimeUtc(path);
                try
                {
                    File.SetLastWriteTimeUtc(path, stamp.AddSeconds(2));
                    workspace.PollExternalChanges();
                    Assert.That(workspace.GetReport(AssetCheckKind.Project).Stale, Is.True);
                }
                finally { File.SetLastWriteTimeUtc(path, stamp); }
            }
        }

        [Test]
        public void IgnoringAnEarlierRowClearsIndexSelection()
        {
            var report = new AssetCheckReport(AssetCheckKind.Texture, "context", AssetCheckPlatform.MiniGame, "Assets");
            report.Issues.Add(Issue("first"));
            var model = new AssetCheckResultsModel();
            model.Bind(report);
            model.Selected.Add(report.Issues[0].TargetId);
            report.Exclude(report.Issues[0].TargetId);
            model.Bind(report);
            Assert.That(model.Selected, Is.Empty);
        }

        [Test]
        public void LocatorResolvesSubAssetsAndIdenticallyNamedHierarchyChildren()
        {
            var path = _root + "/meshes.asset";
            var first = new Mesh { name = "same" };
            var second = new Mesh { name = "same" };
            AssetDatabase.CreateAsset(first, path);
            AssetDatabase.AddObjectToAsset(second, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(second, out string _, out long id);
            Assert.That(AssetCheckLocator.FindSubAsset(path, id), Is.EqualTo(second));
            var root = new GameObject("root");
            try
            {
                new GameObject("same").transform.SetParent(root.transform);
                var child = new GameObject("same");
                child.transform.SetParent(root.transform);
                var component = child.AddComponent<BoxCollider>();
                var location = AssetCheckObjectLocation.Capture(component);
                // 测试对象未必是场景中的第一个根对象，按原生顺序提供根对象列表。
                Assert.That(location.Resolve(root.scene.GetRootGameObjects()), Is.EqualTo(component));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void FixSummaryReportsSavedChangesAndSuccessfulRecheck()
        {
            ReadableTexture();
            using (var workspace = Workspace(AssetCheckKind.Texture))
            {
                var issue = workspace.GetReport(AssetCheckKind.Texture).Issues.Single(i => i.RuleId == "texture.read-write");
                workspace.StartFix(AssetCheckKind.Texture, new[] { issue });
                Drain(workspace);
                Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(1));
                Assert.That(workspace.LastFix.Rechecked, Is.True);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Issues.Any(i => i.RuleId == "texture.read-write"), Is.False);
            }
        }

        [Test]
        public void CancellingFixKeepsSavedChangeAndMarksReportStale()
        {
            var path = ReadableTexture();
            using (var workspace = Workspace(AssetCheckKind.Texture))
            {
                workspace.StartFix(AssetCheckKind.Texture, workspace.GetReport(AssetCheckKind.Texture).Issues
                    .Where(i => i.RuleId == "texture.read-write"));
                workspace.Step();
                workspace.Cancel();
                Assert.That(workspace.LastFix.Cancelled, Is.True);
                Assert.That(workspace.LastFix.ModifiedFiles, Is.EqualTo(1));
                Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, Is.False);
                Assert.That(workspace.GetReport(AssetCheckKind.Texture).Stale, Is.True);
            }
        }

        [Test]
        public void CancellingInsideLargePrefabReleasesTemporarySceneAndDoesNotMarkPartialFilePassed()
        {
            var root = new GameObject("large");
            var path = _root + "/large.prefab";
            try
            {
                for (var index = 0; index < 300; index++) new GameObject("child").transform.SetParent(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config)))
            {
                workspace.ChangeSettings(s => s.scope = _root);
                var count = EditorSceneManager.previewSceneCount;
                workspace.StartScan(AssetCheckKind.PrefabScene);
                workspace.Step(); // 推进到资源收集边界。
                workspace.Step(); // 在层级遍历内部让出执行权。
                Assert.That(workspace.Running, Is.True);
                Assert.That(workspace.GetReport(AssetCheckKind.PrefabScene).CheckedTargets, Is.Empty);
                workspace.Cancel();
                Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(count));
                Assert.That(workspace.GetReport(AssetCheckKind.PrefabScene).Complete, Is.False);
                Assert.That(workspace.GetReport(AssetCheckKind.PrefabScene).PassedAssets, Is.Zero);
            }
        }

        [Test]
        public void IssueLocationSurvivesUnloadingAndReloadingPrefabContents()
        {
            var meshPath = _root + "/mesh.asset";
            AssetDatabase.CreateAsset(new Mesh(), meshPath);
            var root = new GameObject("root");
            var path = _root + "/references.prefab";
            try
            {
                new GameObject("same").transform.SetParent(root.transform);
                var child = new GameObject("same");
                child.transform.SetParent(root.transform);
                child.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var issue = new PrefabSceneCheckModule().Check(path, new PlatformStandards()).Single();
            Assert.That(issue.PropertyPath, Is.EqualTo("m_Mesh"));
            var loaded = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var target = issue.ObjectLocation.Resolve(new[] { loaded });
                Assert.That(target, Is.EqualTo(loaded.transform.GetChild(1).GetComponent<MeshFilter>()));
            }
            finally { PrefabUtility.UnloadPrefabContents(loaded); }
        }

        [Test]
        public void CancellingInsideSceneClosesOnlyTheSceneOpenedByScanner()
        {
            var path = _root + "/large.unity";
            File.WriteAllText(path, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!104 &1\nRenderSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 9\n  m_Fog: 0\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var root = new GameObject("large");
                SceneManager.MoveGameObjectToScene(root, scene);
                for (var index = 0; index < 300; index++)
                    new GameObject("child").transform.SetParent(root.transform);
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                SceneManager.SetActiveScene(active);
            }
            var saved = File.ReadAllBytes(path);
            var count = SceneManager.sceneCount;
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config)))
            {
                workspace.ChangeSettings(s => s.scope = _root);
                workspace.StartScan(AssetCheckKind.PrefabScene);
                workspace.Step();
                workspace.Step();
                Assert.That(SceneManager.GetSceneByPath(path).isLoaded, Is.True);
                workspace.Cancel();
                Assert.That(SceneManager.sceneCount, Is.EqualTo(count));
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
            }
        }

        private AssetCheckWorkspace Workspace(AssetCheckKind kind)
        {
            var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            workspace.ChangeSettings(s => s.scope = _root);
            workspace.StartScan(kind);
            Drain(workspace);
            return workspace;
        }

        private string ReadableTexture()
        {
            var path = Texture();
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true;
            importer.SaveAndReimport();
            return path;
        }

        private Object Pipeline(int mode, bool renderer2D = false)
        {
            var source = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => !path.StartsWith(_root, StringComparison.Ordinal));
            if (source == null) Assert.Ignore("需要真实 URP 资源运行此集成用例。");
            var pipeline = Object.Instantiate(AssetDatabase.LoadMainAssetAtPath(source));
            AssetDatabase.CreateAsset(pipeline, _root + "/pipeline.asset");
            var renderer = ScriptableObject.CreateInstance(renderer2D ? "Renderer2DData" : "UniversalRendererData");
            AssetDatabase.CreateAsset(renderer, _root + "/renderer.asset");
            if (!renderer2D)
                using (var serialized = new SerializedObject(renderer))
                {
                    serialized.FindProperty("m_RenderingMode").intValue = mode;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            using (var serialized = new SerializedObject(pipeline))
            {
                var list = serialized.FindProperty("m_RendererDataList");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return pipeline;
        }

        private string Texture()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var path = _root + "/texture.png";
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var settings = importer.GetPlatformTextureSettings("WebGL");
            settings.overridden = true;
            settings.format = TextureImporterFormat.RGBA32;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();
            return path;
        }

        private static AssetCheckIssue Issue(string name) => new AssetCheckIssue(name, new string('a', 32),
            AssetCheckKind.Texture, "review", "review", "old", "new", true);

        private static void Drain(AssetCheckWorkspace workspace)
        {
            for (var index = 0; workspace.Running && index < 1000; index++) workspace.Step();
            Assert.That(workspace.Failure, Is.Null);
            Assert.That(workspace.Running, Is.False);
        }
    }
}
