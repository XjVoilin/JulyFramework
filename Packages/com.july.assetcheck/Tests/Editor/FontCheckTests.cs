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
using UnityEngine;
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
            var sources = panel.Q<Foldout>("font-missing-sources");
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
