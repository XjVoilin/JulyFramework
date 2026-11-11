using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class FontCheckTests
    {
        private string _root, _config;
        private AssetCheckWorkspace _workspace;
        private FontCheckSession _session;
        private TMP_FontAsset _font;
        private TMP_Settings _tmpSettings, _oldSettings;
        private AssetCheckWindow _window;
        private byte[] _projectSettings;

        [SetUp]
        public void Setup()
        {
            _root = "Assets/__FontCheck_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _config = Path.Combine("Temp", "FontCheck_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_config);
            _workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            _session = new FontCheckSession(() => _workspace, () => { });
            _font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            _font.characterTable.Add(new TMP_Character { unicode = 'A' });
            AssetDatabase.CreateAsset(_font, _root + "/font.asset");
            _oldSettings = (TMP_Settings)typeof(TMP_Settings).GetField("s_Instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            _tmpSettings = ScriptableObject.CreateInstance<TMP_Settings>();
            var serialized = new SerializedObject(_tmpSettings);
            serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = _font;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            typeof(TMP_Settings).GetField("s_Instance", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, _tmpSettings);
            _projectSettings = File.Exists(AssetCheckSettingsFile.FileName) ? File.ReadAllBytes(AssetCheckSettingsFile.FileName) : null;
            // 夹具只检查自己创建的文件，不能把宿主工程的 language JSON 算进结果。
            _session.RefreshSources();
            foreach (var guid in _session.Settings.sourceGuids.ToArray()) _session.RemoveSource(guid);
            _session.SetCharacterFile(null);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_window != null) Object.DestroyImmediate(_window);
            _session.Dispose(); _workspace.Dispose();
            typeof(TMP_Settings).GetField("s_Instance", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, _oldSettings);
            Object.DestroyImmediate(_tmpSettings);
            AssetDatabase.DeleteAsset(_root);
            Directory.Delete(_config, true);
            if (_projectSettings == null) File.Delete(AssetCheckSettingsFile.FileName);
            else File.WriteAllBytes(AssetCheckSettingsFile.FileName, _projectSettings);
        }

        [Test]
        public void ReadsAllStringValuesIncludingNestedEscapesAndSupplementaryCharacters()
        {
            var characters = FontJsonCharacters.Parse("{\"属性名\":\"A\\u6050\\u9f99\\n\\t\\ud83d\\ude00\",\"nested\":[{\"x\":\"字\"},\"2026-11-07\"],\"number\":123,\"null\":null}");
            Assert.That(characters, Is.SupersetOf(new[] { (uint)'恐', (uint)'龙', 0x1F600u, (uint)'字', (uint)'-' }));
            Assert.That(characters.Intersect(new[] { (uint)'属', (uint)'\n', (uint)'\t' }), Is.Empty);
            Assert.That(characters.Count(value => value == 0x1F600u), Is.EqualTo(1));
        }

        [TestCase("{invalid}")]
        [TestCase("{\"a\":\"甲\",\"a\":\"乙\"}")]
        [TestCase("{} {}")]
        public void InvalidJsonDoesNotProduceAPassingCharacterSet(string json) =>
            Assert.Throws<JsonReaderException>(() => FontJsonCharacters.Parse(json));

        [Test]
        public void DiscoveryIsCaseInsensitiveAndRemovalPersistsThroughRefreshAndReload()
        {
            var source = Json("myLANGUAGE.json", "[\"龙\"]");
            var manual = Json("extra.json", "[\"恐\"]");
            _session.RefreshSources();
            var guid = AssetDatabase.AssetPathToGUID(source);
            Assert.That(_session.Settings.sourceGuids, Does.Contain(guid));
            Assert.That(_session.Settings.sourceGuids, Does.Not.Contain(AssetDatabase.AssetPathToGUID(manual)));
            _session.RemoveSource(guid);
            _session.RefreshSources();
            Assert.That(_session.Settings.sourceGuids, Does.Not.Contain(guid));
            var persisted = new AssetCheckSettingsFile(_config).LoadOrCreate();
            Assert.That(persisted.fonts.excludedSourceGuids, Does.Contain(guid));
            _session.AddSource(source); _session.AddSource(source); _session.AddSource(manual);
            Assert.That(_session.Settings.sourceGuids.Count(value => value == guid), Is.EqualTo(1));
            Assert.That(_session.Settings.excludedSourceGuids, Does.Not.Contain(guid));
        }

        [Test]
        public void MissingCharacterFileAndMissingFontAreIndependentAndCheckingDoesNotMutateAssets()
        {
            var source = Json("language.json", "[\"A恐龙😀\",\"龙\"]");
            var characters = Json("characters.json", "[\"A恐\"]");
            _session.AddSource(source); _session.SetCharacterFile(characters);
            var bytes = File.ReadAllBytes(_root + "/font.asset");
            var sourceBytes = File.ReadAllBytes(source);
            var characterBytes = File.ReadAllBytes(characters);
            Check();
            Assert.That(FontCheckResult.CopyText(_session.Result.CharacterFileMissing), Is.EqualTo("龙😀"));
            Assert.That(FontCheckResult.CopyText(_session.Result.FontMissing), Is.EqualTo("恐龙😀"));
            Assert.That(_session.Result.FontMissing.Single(entry => entry.Text == "龙").SourcePaths, Does.Contain(source));
            Assert.That(FontCheckResult.CopyText(_session.Result.MissingCharacters), Is.EqualTo("恐龙😀"));
            _session.CopyMissing();
            Assert.That(EditorGUIUtility.systemCopyBuffer, Is.EqualTo("恐龙😀"));
            Assert.That(_font.characterTable.Count, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(_root + "/font.asset"), Is.EqualTo(bytes));
            Assert.That(File.ReadAllBytes(source), Is.EqualTo(sourceBytes));
            Assert.That(File.ReadAllBytes(characters), Is.EqualTo(characterBytes));
        }

        [Test]
        public void SupplementingCharacterFileThenRebuildingFontClearsTheTwoFailuresInOrder()
        {
            var source = Json("language.json", "[\"A恐龙\"]");
            var characters = Json("characters.json", "[\"A\"]");
            _session.AddSource(source); _session.SetCharacterFile(characters); Check();
            Assert.That(_session.Result.CharacterFileMissing.Length, Is.EqualTo(2));
            File.WriteAllText(characters, "[\"A恐龙\"]");
            AssetDatabase.ImportAsset(characters, ImportAssetOptions.ForceSynchronousImport);
            Assert.That(_session.Stale, Is.True);
            Assert.Throws<InvalidOperationException>(() => _session.CopyMissing());
            Check();
            Assert.That(_session.Result.CharacterFileMissing, Is.Empty);
            Assert.That(_session.Result.FontMissing.Length, Is.EqualTo(2));
            _font.characterTable.Add(new TMP_Character { unicode = '恐' });
            _font.characterTable.Add(new TMP_Character { unicode = '龙' });
            Check(); Assert.That(_session.Result.FontMissing, Is.Empty);
        }

        [Test]
        public void DefaultFontChangesAndCancellationCannotLeaveACopyablePassingResult()
        {
            _session.AddSource(Json("language.json", "[\"A龙\"]"));
            _session.SetCharacterFile(Json("characters.json", "[\"A龙\"]"));
            Check();
            var serialized = new SerializedObject(_tmpSettings);
            serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _session.SynchronizeSettings();
            Assert.That(_session.Stale, Is.True);
            Assert.Throws<InvalidOperationException>(() => _session.CopyMissing());
            Assert.Throws<InvalidOperationException>(_session.Start);
            serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = _font;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _session.Start(); _session.Cancel();
            Assert.That(_session.Running, Is.False);
            Assert.That(_session.Result, Is.Null);
        }

        [Test]
        public void InvalidSourceFailsTheWholeCheckWithItsPath()
        {
            var source = Json("language.json", "invalid");
            _session.AddSource(source); _session.SetCharacterFile(Json("characters.json", "[\"A\"]"));
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("JsonReaderException: Unexpected character"));
            _session.Start(); while (_session.Running) _session.Pump();
            Assert.That(_session.Result, Is.Null);
            Assert.That(_session.Failure, Does.Contain(source));
        }

        [UnityTest]
        public IEnumerator WindowShowsOneMissingListWithExpandableSourcesAndSharesOperationLocks()
        {
            var source = Json("language.json", "[\"A恐龙\"]");
            var characters = Json("characters.json", "[\"A恐\"]");
            var settings = AssetCheckSettings.CreateDefault();
            settings.fonts.initialized = true;
            settings.fonts.sourceGuids.Add(AssetDatabase.AssetPathToGUID(source));
            settings.fonts.characterFileGuid = AssetDatabase.AssetPathToGUID(characters);
            settings.fonts.excludedSourceGuids = _session.Settings.excludedSourceGuids.ToList();
            File.WriteAllText(AssetCheckSettingsFile.FileName, JsonUtility.ToJson(settings, true));
            _window = ScriptableObject.CreateInstance<AssetCheckWindow>();
            _window.Show();
            for (var index = 0; _window.rootVisualElement.Q<FontCheckPanel>() == null && index < 100; index++) yield return null;
            _window.SelectTool(AssetCheckWindow.ToolPage.FontCheck);
            var panel = _window.rootVisualElement.Q<FontCheckPanel>();
            panel.Session.Start();
            Assert.That(_window.FolderOperationRunning, Is.True);
            Assert.Throws<InvalidOperationException>(() => _window.SelectTool(AssetCheckWindow.ToolPage.ResourceCheck));
            for (var index = 0; panel.Running && index < 100; index++) yield return null;
            Assert.That(panel.Running, Is.False);
            Assert.That(panel.Q<TextField>("font-missing").value, Is.EqualTo("恐龙"));
            Assert.That(panel.Q<VisualElement>("font-results").Query<Button>("font-copy-missing").ToList().Count, Is.EqualTo(1));
            var sources = panel.Q<Foldout>("font-coverage").Q<Foldout>("font-missing-sources");
            Assert.That(sources.value, Is.False);
            Assert.That(sources.text, Does.Contain("1 个文件"));
            sources.value = true;
            Assert.That(sources.Q<Button>("font-locate-missing-source").tooltip, Is.EqualTo(source));
            Assert.That(sources.Q<TextField>("font-source-missing").value, Is.EqualTo("恐龙"));
            Assert.That(panel.Q<Button>("font-copy-missing").enabledInHierarchy, Is.True);
            Assert.That(panel.Q<Button>("font-add-source").text, Is.EqualTo("添加 JSON 文件"));
            Assert.That(panel.Q<UnityEditor.UIElements.ObjectField>("font-add-source"), Is.Null);
            Assert.That(_window.rootVisualElement.Q<VisualElement>("resource-check-page").style.display.value, Is.EqualTo(DisplayStyle.None));
            // 无效拖入必须恢复为实际配置的资源，不能让界面展示与扫描输入不一致。
            var invalid = Json("extra.txt", "text");
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("ArgumentException: 请选择 Assets 下的 JSON 文本资源"));
            panel.Q<UnityEditor.UIElements.ObjectField>("font-character-file").value = AssetDatabase.LoadAssetAtPath<TextAsset>(invalid);
            Assert.That(AssetDatabase.GetAssetPath(panel.Q<UnityEditor.UIElements.ObjectField>("font-character-file").value), Is.EqualTo(characters));
            panel.Session.Start();
            Object.DestroyImmediate(_window); _window = null;
            Assert.That(panel.Running, Is.False);
        }

        [Test]
        public void SerializedTextHandlesCommonTagsNoParseEscapesAndSupplementaryCharacters()
        {
            var characters = FontSerializedText.Characters("<color=#fff><link=\"id\">A龙😀</link></color>\\n<sprite=0>", true, true, out var limitation);
            Assert.That(characters, Is.EquivalentTo(new[] { (uint)'A', (uint)'龙', 0x1f600u }));
            Assert.That(limitation, Is.Null);
            var literal = FontSerializedText.Characters("<b>A</b>\\n", false, false, out _);
            Assert.That(literal, Does.Contain((uint)'<'));
            Assert.That(literal, Does.Contain((uint)'n'));
            var noParse = FontSerializedText.Characters("<noparse><b>A</b></noparse>", true, true, out _);
            Assert.That(noParse, Is.EquivalentTo(FontJsonCharacters.FromText("<b>A</b>")));
            FontSerializedText.Characters("<font=\"Other\">字</font>", true, true, out limitation);
            Assert.That(limitation, Is.Not.Empty);
        }

        [Test]
        public void PrefabsUseTheirOwnFontsAndDoNotRequireAssetTextInTheJsonCharacterFile()
        {
            var other = ScriptableObject.CreateInstance<TMP_FontAsset>();
            other.name = "Other";
            other.characterTable.Add(new TMP_Character { unicode = '龙' });
            AssetDatabase.CreateAsset(other, _root + "/other.asset");
            var first = Prefab("first", "A恐", _font);
            var second = Prefab("second", "龙恐", other);
            var json = Json("text.json", "[\"A\"]");
            _session.AddSource(json); _session.SetCharacterFile(json); _session.AddScanFolder(_root);
            AssetDatabase.SaveAssets();
            var paths = new[] { first, second, _root + "/font.asset", _root + "/other.asset" };
            var before = paths.ToDictionary(path => path, File.ReadAllBytes);
            Check();
            Assert.That(_session.Result.AssetSourceCount, Is.EqualTo(2));
            Assert.That(_session.Result.TextCount, Is.EqualTo(2)); // 两个 Prefab 的根对象均未激活。
            Assert.That(_session.Result.CharacterFileMissing, Is.Empty);
            Assert.That(_session.Result.Fonts.Length, Is.EqualTo(2));
            foreach (var coverage in _session.Result.Fonts)
                Assert.That(FontCheckResult.CopyText(coverage.Missing), Is.EqualTo("恐"));
            Assert.That(_session.Result.MissingCharacters.Single().SourcePaths, Is.EquivalentTo(new[] { first, second }));
            foreach (var path in paths) Assert.That(File.ReadAllBytes(path), Is.EqualTo(before[path]));
            Json("unrelated.json", "[\"A\"]"); // 不属于来源的 JSON 不影响结果。
            Assert.That(_session.Stale, Is.False);
            Prefab("new", "A", _font);
            Assert.That(_session.Stale, Is.True);
        }

        [Test]
        public void AssetOnlyScanAllowsAnExplicitFontWithoutDefaultFontOrCharacterFile()
        {
            Prefab("explicit", "A", _font);
            _session.AddScanFolder(_root);
            using (var serialized = new SerializedObject(_tmpSettings))
            {
                serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Check();
            Assert.That(_session.Result.JsonSourceCount, Is.Zero);
            Assert.That(_session.Result.MissingCharacters, Is.Empty);
            Assert.That(_session.Result.Unverified, Is.Empty);
        }

        [Test]
        public void SceneInstanceOverridesAreReadAndSceneSetupAndSourceBytesArePreserved()
        {
            var prefab = Prefab("nested", "A", _font);
            var path = _root + "/override.unity";
            WithTemporaryScene(scene =>
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), scene);
                SetText(instance.GetComponent<TMP_Text>(), "龙", _font);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<TMP_Text>());
                Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
            });
            var before = File.ReadAllBytes(path);
            var count = SceneManager.sceneCount;
            var previews = EditorSceneManager.previewSceneCount;
            var active = SceneManager.GetActiveScene();
            var selection = Selection.objects;
            var texts = FontAssetTextScan.Read(path, _font);
            Assert.That(texts.Single().Characters, Is.EquivalentTo(new[] { (uint)'龙' }));
            Assert.That(texts.Single().Source.Location, Is.Not.Null);
            Assert.That(SceneManager.sceneCount, Is.EqualTo(count));
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previews));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
            Assert.That(Selection.objects, Is.EqualTo(selection));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void VariantsAndNestedPrefabsUseEffectiveTextAndOverlappingFoldersScanFilesOnce()
        {
            var prefab = Prefab("base", "A", _font);
            AssetDatabase.CreateFolder(_root, "Sub");
            WithTemporaryScene(scene =>
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), scene);
                SetText(instance.GetComponent<TMP_Text>(), "龙", _font);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<TMP_Text>());
                PrefabUtility.SaveAsPrefabAsset(instance, _root + "/Sub/variant.prefab");
                var parent = new GameObject("Parent");
                instance.transform.SetParent(parent.transform);
                PrefabUtility.SaveAsPrefabAsset(parent, _root + "/Sub/parent.prefab");
            });
            _session.AddScanFolder(_root); _session.AddScanFolder(_root + "/Sub");
            Check();
            Assert.That(_session.Result.AssetSourceCount, Is.EqualTo(3));
            Assert.That(_session.Result.FontMissing.Single().SourcePaths.Length, Is.EqualTo(2));
            var persisted = new AssetCheckSettingsFile(_config).LoadOrCreate();
            Assert.That(persisted.fonts.scanFolderGuids.Count, Is.EqualTo(2));
        }

        [Test]
        public void UnresolvedRichTextAndDirtyScenesAreUnverifiedAndCancellationReleasesTemporaryContents()
        {
            Prefab("unresolved", "<font=\"Other\">龙</font>", _font);
            _session.AddScanFolder(_root);
            Check();
            Assert.That(_session.Result.Unverified.Length, Is.EqualTo(1));
            Assert.That(_session.Result.MissingCharacters, Is.Empty);
            var previews = EditorSceneManager.previewSceneCount;
            var count = SceneManager.sceneCount;
            _session.Start(); _session.Pump(); _session.Cancel();
            Assert.That(_session.Result, Is.Null);
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previews));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(count));
            WithTemporaryScene(scene =>
            {
                var path = _root + "/dirty.unity";
                Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
                EditorSceneManager.MarkSceneDirty(scene);
                var result = FontAssetTextScan.Read(path, _font);
                Assert.That(result.Single().Source.Limitation, Does.Contain("未保存"));
                Assert.That(scene.isLoaded && scene.isDirty, Is.True);
            });
        }

        [Test]
        public void PanelClearsAnEarlierScanFailureAfterSuccessfulRetry()
        {
            var source = Json("text.json", "invalid");
            _session.AddSource(source); _session.SetCharacterFile(source);
            using var panel = new FontCheckPanel(() => _workspace, () => { });
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("JsonReaderException: Unexpected character"));
            panel.Session.Start();
            while (panel.Running) panel.Pump();
            Assert.That(panel.Q<Label>("font-error").text, Is.Not.Empty);
            File.WriteAllText(source, "[\"A\"]");
            AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceSynchronousImport);
            panel.Session.Start();
            while (panel.Running) panel.Pump();
            Assert.That(panel.Q<Label>("font-error").text, Is.Empty);
            Assert.That(panel.Session.Result.MissingCharacters, Is.Empty);
        }

        private string Prefab(string name, string content, TMP_FontAsset font)
        {
            var path = _root + "/" + name + ".prefab";
            WithTemporaryScene(scene =>
            {
                var root = new GameObject(name, typeof(RectTransform));
                root.SetActive(false);
                SetText(root.AddComponent<TextMeshProUGUI>(), content, font);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            });
            return path;
        }

        private static void SetText(TMP_Text text, string content, TMP_FontAsset font)
        {
            using var serialized = new SerializedObject(text);
            serialized.FindProperty("m_text").stringValue = content;
            serialized.FindProperty("m_fontAsset").objectReferenceValue = font;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WithTemporaryScene(Action<Scene> action)
        {
            var active = SceneManager.GetActiveScene();
            var selection = Selection.objects;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try { SceneManager.SetActiveScene(scene); action(scene); }
            finally
            {
                SceneManager.SetActiveScene(active); Selection.objects = selection;
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private string Json(string name, string text)
        {
            var path = _root + "/" + name;
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        private void Check()
        {
            _session.Start();
            for (var index = 0; _session.Running && index < 100; index++) _session.Pump();
            Assert.That(_session.Running, Is.False);
            Assert.That(_session.Failure, Is.Null);
            Assert.That(_session.Result, Is.Not.Null);
        }
    }
}
