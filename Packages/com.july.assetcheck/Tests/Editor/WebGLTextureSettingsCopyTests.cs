using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class WebGLTextureSettingsCopyTests
    {
        private string _root, _config;

        [SetUp]
        public void Setup()
        {
            _root = "Assets/__WebGLCopy_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _config = Path.GetFullPath("Temp/WebGLCopy_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_config);
            TextureBatchImportProbe.Root = _root;
            TextureBatchImportProbe.Batches.Clear();
        }

        [TearDown]
        public void Cleanup()
        {
            TextureBatchImportProbe.Root = null;
            TextureBatchImportProbe.Batches.Clear();
            AssetDatabase.DeleteAsset(_root);
            Assert.That(_config, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar));
            Directory.Delete(_config, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TextureCopiesCompleteWebGLSettingsAndPreservesSourceAndOtherPlatforms(bool existingTargets)
        {
            var path = Texture("source");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var source = ConfigureWebGL(importer);
            if (existingTargets)
                foreach (var target in new[] { "MiniGame", "WeixinMiniGame" })
                    TuanjieTextureSettings.WriteFormat(importer, target, TextureImporterFormat.ASTC_6x6);
            importer.SaveAndReimport();
            var defaults = JsonUtility.ToJson(importer.GetDefaultPlatformTextureSettings());
            var android = JsonUtility.ToJson(importer.GetPlatformTextureSettings("Android"));
            var web = JsonUtility.ToJson(importer.GetPlatformTextureSettings("WebGL"));

            Assert.That(WebGLTextureSettingsCopy.CopyAsset(path), Is.True);
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            AssertTargets(source, importer.GetPlatformTextureSettings);
            Assert.That(JsonUtility.ToJson(importer.GetPlatformTextureSettings("WebGL")), Is.EqualTo(web));
            Assert.That(JsonUtility.ToJson(importer.GetDefaultPlatformTextureSettings()), Is.EqualTo(defaults));
            Assert.That(JsonUtility.ToJson(importer.GetPlatformTextureSettings("Android")), Is.EqualTo(android));
            Assert.That(importer.isReadable, Is.True);
            TextureBatchImportProbe.Batches.Clear();
            Assert.That(WebGLTextureSettingsCopy.CopyAsset(path), Is.False);
            Assert.That(TextureBatchImportProbe.Batches, Is.Empty, "相同配置不重复导入");
        }

        [Test]
        public void TextureWithoutWebGLOverrideCopiesDefaultIncludingAutomatic()
        {
            var path = Texture("default");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var defaults = importer.GetDefaultPlatformTextureSettings();
            defaults.maxTextureSize = 128;
            defaults.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(defaults);
            var dormant = importer.GetPlatformTextureSettings("WebGL");
            dormant.overridden = false;
            dormant.maxTextureSize = 2048;
            importer.SetPlatformTextureSettings(dormant);
            importer.SaveAndReimport();
            var web = JsonUtility.ToJson(importer.GetPlatformTextureSettings("WebGL"));

            WebGLTextureSettingsCopy.CopyAsset(path);
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            AssertTargets(defaults, importer.GetPlatformTextureSettings);
            Assert.That(JsonUtility.ToJson(importer.GetPlatformTextureSettings("WebGL")), Is.EqualTo(web));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AtlasCopiesEffectiveWebGLAndPreservesPackingAndTextureOptions(bool overridden)
        {
            var atlas = new SpriteAtlas();
            var path = _root + "/atlas.spriteatlas";
            AssetDatabase.CreateAsset(atlas, path);
            var settings = atlas.GetPlatformSettings(overridden ? "WebGL" : "DefaultTexturePlatform");
            settings.overridden = overridden;
            settings.maxTextureSize = 512;
            settings.format = overridden ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.Automatic;
            settings.compressionQuality = 83;
            settings.textureCompression = TextureImporterCompression.CompressedHQ;
            atlas.SetPlatformSettings(settings);
            var source = AtlasPlatformSettings.Effective(atlas, "WebGL");
            var web = JsonUtility.ToJson(atlas.GetPlatformSettings("WebGL"));
            var defaults = JsonUtility.ToJson(atlas.GetPlatformSettings("DefaultTexturePlatform"));
            var android = JsonUtility.ToJson(atlas.GetPlatformSettings("Android"));
            var packing = JsonUtility.ToJson(atlas.GetPackingSettings());
            var texture = JsonUtility.ToJson(atlas.GetTextureSettings());
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssetIfDirty(atlas);

            Assert.That(WebGLTextureSettingsCopy.CopyAsset(path), Is.True);
            atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            AssertTargets(source, atlas.GetPlatformSettings);
            Assert.That(JsonUtility.ToJson(atlas.GetPlatformSettings("WebGL")), Is.EqualTo(web));
            Assert.That(JsonUtility.ToJson(atlas.GetPlatformSettings("DefaultTexturePlatform")), Is.EqualTo(defaults));
            Assert.That(JsonUtility.ToJson(atlas.GetPlatformSettings("Android")), Is.EqualTo(android));
            Assert.That(JsonUtility.ToJson(atlas.GetPackingSettings()), Is.EqualTo(packing));
            Assert.That(JsonUtility.ToJson(atlas.GetTextureSettings()), Is.EqualTo(texture));
            Assert.That(WebGLTextureSettingsCopy.CopyAsset(path), Is.False);
        }

        [Test]
        public void CollectDeduplicatesFoldersAndFilesAndRejectsOutsideAssets()
        {
            AssetDatabase.CreateFolder(_root, "nested");
            var first = Texture("one");
            var second = Texture("nested/two");
            var atlas = _root + "/atlas.spriteatlas";
            AssetDatabase.CreateAsset(new SpriteAtlas(), atlas);
            var nativeTexture = _root + "/generated.asset";
            AssetDatabase.CreateAsset(new Texture2D(4, 4), nativeTexture);
            Assert.That(WebGLTextureSettingsCopy.Collect(new[] { _root, first, _root + "/nested" }),
                Is.EquivalentTo(new[] { first, second, atlas }));
            Assert.Throws<ArgumentException>(() => WebGLTextureSettingsCopy.Collect(new[] { "Packages" }));
            Assert.Throws<ArgumentException>(() => WebGLTextureSettingsCopy.Collect(new[] { nativeTexture }));
        }

        [Test]
        public void BatchImportsTwentyThenFiveAndCanCancelBetweenBatches()
        {
            var paths = Enumerable.Range(0, 25).Select(i => Texture("batch" + i)).ToArray();
            var operation = new WebGLTextureSettingsCopy(paths, AssetCheckSettings.CreateDefault);
            TextureBatchImportProbe.Batches.Clear();
            operation.Step();
            Assert.That(operation.Processed, Is.EqualTo(20));
            Assert.That(operation.Modified, Is.EqualTo(20));
            Assert.That(TextureBatchImportProbe.Batches.Select(batch => batch.Length), Is.EqualTo(new[] { 20 }));
            operation.Cancel();
            operation.Step();
            Assert.That(operation.Processed, Is.EqualTo(20));
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(paths[24])).GetPlatformTextureSettings("MiniGame").overridden, Is.False);

            var remaining = new WebGLTextureSettingsCopy(paths, AssetCheckSettings.CreateDefault);
            remaining.Step();
            Assert.That(remaining.Unchanged, Is.EqualTo(20));
            remaining.Step();
            Assert.That(remaining.Modified, Is.EqualTo(5));
            Assert.That(remaining.Running, Is.False);
            Assert.That(TextureBatchImportProbe.Batches.Select(batch => batch.Length), Is.EqualTo(new[] { 20, 5 }));
        }

        [Test]
        public void BatchFailureRestoresImportsAndMovedAssetsStillUseTheirGuid()
        {
            var first = Texture("first");
            var deleted = Texture("deleted");
            var operation = new WebGLTextureSettingsCopy(new[] { first, deleted }, AssetCheckSettings.CreateDefault);
            var moved = _root + "/moved.png";
            Assert.That(AssetDatabase.MoveAsset(first, moved), Is.Empty);
            AssetDatabase.DeleteAsset(deleted);
            Assert.Throws<IOException>(() => operation.Step());
            Assert.That(operation.Modified, Is.EqualTo(1));
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(moved)).GetPlatformTextureSettings("MiniGame").overridden, Is.True);
            var after = Texture("afterFailure");
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(after), Is.Not.Null);
        }

        [Test]
        public void WindowBindsScopeAndCopyControlsWithoutModifyingAssetsOnOpen()
        {
            var path = Texture("window");
            var before = File.ReadAllText(path + ".meta");
            var shared = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            shared.ChangeSettings(settings => settings.scope = _root);
            var host = ScriptableObject.CreateInstance<EditorWindow>();
            var window = new WebGLTextureSettingsCopyPanel(() => shared, () => { });
            host.rootVisualElement.Add(window);
            window.SynchronizeSettings();
            try
            {
                Assert.That(window.Query<Button>().ToList().Select(button => button.text),
                    Does.Contain("复制全部差异"));
                Assert.That(File.ReadAllText(path + ".meta"), Is.EqualTo(before));
            }
            finally { window.Dispose(); shared.Dispose(); Object.DestroyImmediate(host); }
        }

        [Test]
        public void PreviewReadsInheritedSourceAndDetectsQualityDifferencesWithoutWriting()
        {
            var path = Texture("preview");
            WebGLTextureSettingsCopy.CopyAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var mini = importer.GetPlatformTextureSettings("MiniGame");
            mini.compressionQuality = 71;
            TuanjieTextureSettings.WriteSettings(importer, mini);
            importer.SaveAndReimport();
            var before = File.ReadAllBytes(path + ".meta");
            var entry = WebGLTextureSettingsCopy.Inspect(path);
            Assert.That(entry.Inherited, Is.True);
            Assert.That(entry.CopyMiniGame, Is.True);
            Assert.That(entry.CopyWeixinMiniGame, Is.False);
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(before));
        }

        [Test]
        public void WindowListsOnlyAfterExplicitReadOnlyScanAndRefreshesAfterCopy()
        {
            var path = Texture("listing");
            var atlasPath = _root + "/atlas.spriteatlas";
            AssetDatabase.CreateAsset(new SpriteAtlas(), atlasPath);
            var before = File.ReadAllBytes(path + ".meta");
            var atlasBefore = File.ReadAllBytes(atlasPath);
            var shared = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            shared.ChangeSettings(settings => settings.scope = _root);
            var host = ScriptableObject.CreateInstance<EditorWindow>();
            var window = new WebGLTextureSettingsCopyPanel(() => shared, () => { });
            host.rootVisualElement.Add(window);
            window.SynchronizeSettings();
            try
            {
                window.SynchronizeSettings();
                var root = window;
                var list = root.Q<MultiColumnListView>("results");
                var copy = root.Q<Button>("copy-all");
                Assert.That(list.itemsSource.Count, Is.Zero);
                Assert.That(copy.enabledInHierarchy, Is.False);
                Assert.Throws<InvalidOperationException>(() => window.StartCopy(false));
                window.StartScan();
                Assert.That(copy.enabledInHierarchy, Is.False);
                DrainWindow(window);
                Assert.That(list.itemsSource.Count, Is.EqualTo(2));
                Assert.That(copy.enabledInHierarchy, Is.True);
                Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(before));
                Assert.That(File.ReadAllBytes(atlasPath), Is.EqualTo(atlasBefore));
                Assert.That(((WebGLTextureSettingsCopyEntry)list.itemsSource[0]).NeedsCopy, Is.True);
                window.StartCopy(false);
                DrainWindow(window);
                Assert.That(list.itemsSource.Count, Is.Zero, "默认只列出差异，复制后复查应无差异");
                Assert.That(copy.enabledInHierarchy, Is.False);
                Assert.That(root.Q<Label>("status").text, Does.Contain("修改 2 个文件"));
                Assert.That(WebGLTextureSettingsCopy.Inspect(path).NeedsCopy, Is.False);
            }
            finally { window.Dispose(); shared.Dispose(); Object.DestroyImmediate(host); }
        }

        [Test]
        public void WindowSelectionCopiesOnlyCheckedFilesAndFilterRetainsSelection()
        {
            var first = Texture("first-selected");
            var second = Texture("second-unselected");
            var shared = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            shared.ChangeSettings(settings => settings.scope = _root);
            var host = ScriptableObject.CreateInstance<EditorWindow>();
            var window = new WebGLTextureSettingsCopyPanel(() => shared, () => { });
            host.rootVisualElement.Add(window);
            window.SynchronizeSettings();
            try
            {
                host.Show();
                window.SynchronizeSettings();
                window.StartScan();
                DrainWindow(window);
                var root = window;
                var list = root.Q<MultiColumnListView>("results");
                var column = list.columns["resource"];
                var cell = column.makeCell();
                column.bindCell(cell, 0);
                root.Add(cell);
                cell.Q<Toggle>().value = true;
                Assert.That(root.Q<Button>("copy-selected").enabledInHierarchy, Is.True);
                root.Q<Toggle>("only-differences").value = false;
                Assert.That(list.itemsSource.Count, Is.EqualTo(2));
                window.StartCopy(true);
                DrainWindow(window);
                Assert.That(WebGLTextureSettingsCopy.Inspect(first).NeedsCopy, Is.False);
                Assert.That(WebGLTextureSettingsCopy.Inspect(second).NeedsCopy, Is.True);
                Assert.That(list.itemsSource.Count, Is.EqualTo(2));
            }
            finally { window.Dispose(); shared.Dispose(); Object.DestroyImmediate(host); }
        }

        [Test]
        public void WindowInvalidatesOnSourceChangeAndFolderChangeRequiresNewScan()
        {
            var path = Texture("invalidate");
            var shared = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            shared.ChangeSettings(settings => settings.scope = _root);
            var host = ScriptableObject.CreateInstance<EditorWindow>();
            var window = new WebGLTextureSettingsCopyPanel(() => shared, () => { });
            host.rootVisualElement.Add(window);
            window.SynchronizeSettings();
            try
            {
                window.SynchronizeSettings();
                window.StartScan();
                DrainWindow(window);
                Assert.That(window.Q<Button>("copy-all").enabledInHierarchy, Is.True);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                ConfigureWebGL(importer);
                importer.SaveAndReimport();
                Assert.That(window.Q<Button>("copy-all").enabledInHierarchy, Is.False);
                Assert.That(window.Q<Label>("status").text, Does.Contain("过期"));
                Assert.Throws<InvalidOperationException>(() => window.StartCopy(false));
                window.StartScan();
                DrainWindow(window);
                Assert.That(window.Q<Button>("copy-all").enabledInHierarchy, Is.True);
                AssetDatabase.CreateFolder(_root, "empty");
                shared.ChangeSettings(settings => settings.scope = _root + "/empty");
                window.SynchronizeSettings();
                Assert.That(window.Q<MultiColumnListView>("results").itemsSource.Count, Is.Zero);
                Assert.That(window.Q<Button>("copy-all").enabledInHierarchy, Is.False);
                window.StartScan();
                DrainWindow(window);
                Assert.That(window.Q<Label>("status").text, Does.Contain("扫描完成"));
            }
            finally { window.Dispose(); shared.Dispose(); Object.DestroyImmediate(host); }
        }

        [Test]
        public void WindowCancelsReadOnlyScanAndStopsCopyBetweenBatches()
        {
            var paths = Enumerable.Range(0, 25).Select(i => Texture("cancel" + i.ToString("D2"))).ToArray();
            var before = paths.Select(path => File.ReadAllText(path + ".meta")).ToArray();
            var shared = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config));
            shared.ChangeSettings(settings => settings.scope = _root);
            var host = ScriptableObject.CreateInstance<EditorWindow>();
            var window = new WebGLTextureSettingsCopyPanel(() => shared, () => { });
            host.rootVisualElement.Add(window);
            window.SynchronizeSettings();
            try
            {
                window.SynchronizeSettings();
                window.StartScan();
                window.Pump();
                window.Cancel();
                Assert.That(window.Q<Button>("copy-all").enabledInHierarchy, Is.False);
                Assert.That(paths.Select(path => File.ReadAllText(path + ".meta")), Is.EqualTo(before));
                window.StartScan();
                DrainWindow(window);
                window.StartCopy(false);
                window.Pump();
                window.Cancel();
                window.Pump();
                Assert.That(paths.Count(path => !WebGLTextureSettingsCopy.Inspect(path).NeedsCopy), Is.EqualTo(20));
                Assert.That(window.Q<Button>("copy-all").enabledInHierarchy, Is.False);
                Assert.That(window.Q<Label>("status").text, Does.Contain("已处理 20/25"));
            }
            finally { window.Dispose(); shared.Dispose(); Object.DestroyImmediate(host); }
        }

        [Test]
        public void CopyJobSkipsExplicitFilesAndRecursiveFoldersIncludingAtlases()
        {
            AssetDatabase.CreateFolder(_root, "Skip");
            AssetDatabase.CreateFolder(_root, "SkipOther");
            var single = Texture("single-ignore");
            var nested = Texture("Skip/nested");
            var included = Texture("SkipOther/included");
            var atlas = _root + "/Skip/atlas.spriteatlas";
            AssetDatabase.CreateAsset(new SpriteAtlas(), atlas);
            var ignoredPaths = new[] { single + ".meta", nested + ".meta", atlas, atlas + ".meta" };
            var before = ignoredPaths.Select(File.ReadAllBytes).ToArray();
            var settings = AssetCheckSettings.CreateDefault();
            settings.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(single), lastKnownPath = single });
            settings.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(_root + "/Skip"), lastKnownPath = _root + "/Skip" });
            var operation = new WebGLTextureSettingsCopy(WebGLTextureSettingsCopy.Collect(new[] { _root }), () => settings);
            operation.Step();
            Assert.That(operation.Processed, Is.EqualTo(4));
            Assert.That(operation.Modified, Is.EqualTo(1));
            Assert.That(operation.Ignored, Is.EqualTo(3));
            Assert.That(operation.Unchanged, Is.Zero);
            Assert.That(WebGLTextureSettingsCopy.Inspect(included).NeedsCopy, Is.False);
            for (var index = 0; index < ignoredPaths.Length; index++)
                Assert.That(File.ReadAllBytes(ignoredPaths[index]), Is.EqualTo(before[index]), ignoredPaths[index]);
        }

        [Test]
        public void CopyJobRechecksIgnorePolicyBeforeWritingTheNextBatch()
        {
            var paths = Enumerable.Range(0, 25).Select(index => Texture("policy" + index.ToString("D2"))).ToArray();
            var settings = AssetCheckSettings.CreateDefault();
            var operation = new WebGLTextureSettingsCopy(paths, () => settings);
            operation.Step();
            var before = paths.Skip(20).Select(path => File.ReadAllBytes(path + ".meta")).ToArray();
            settings.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(_root), lastKnownPath = _root });
            operation.Step();
            Assert.That(operation.Modified, Is.EqualTo(20));
            Assert.That(operation.Ignored, Is.EqualTo(5));
            Assert.That(operation.Unchanged, Is.Zero);
            for (var index = 20; index < 25; index++)
                Assert.That(File.ReadAllBytes(paths[index] + ".meta"), Is.EqualTo(before[index - 20]));
        }

        [Test]
        public void AddedAndRestoredSingleFileIgnoreRequireANewCopyScan()
        {
            var path = Texture("live-ignore");
            using (var shared = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config)))
            {
                shared.ChangeSettings(settings => settings.scope = _root);
                using (var panel = new WebGLTextureSettingsCopyPanel(() => shared, () => { }))
                {
                    panel.StartScan(); DrainWindow(panel);
                    var guid = AssetDatabase.AssetPathToGUID(path);
                    shared.Ignore(new AssetCheckIssue(path, guid, AssetCheckKind.Texture, "test", "测试", "当前", "要求", false));
                    Assert.Throws<InvalidOperationException>(() => panel.StartCopy(false));
                    Assert.That(panel.Q<MultiColumnListView>("results").itemsSource.Count, Is.Zero);
                    panel.StartScan(); DrainWindow(panel);
                    Assert.That(panel.Q<Label>("counts").text, Does.Contain("忽略 1 个"));
                    shared.Restore(guid);
                    Assert.Throws<InvalidOperationException>(() => panel.StartCopy(false));
                    panel.StartScan(); DrainWindow(panel);
                    Assert.That(panel.Q<MultiColumnListView>("results").itemsSource.Count, Is.EqualTo(1));
                    Assert.That(panel.Q<Label>("counts").text, Does.Contain("忽略 0 个"));
                }
            }
        }

        [Test]
        public void ExternalIgnoreConfigurationChangeStopsCopyBeforeFurtherWrites()
        {
            var paths = Enumerable.Range(0, 25).Select(index => Texture("external" + index.ToString("D2"))).ToArray();
            using (var shared = new AssetCheckWorkspace(new AssetCheckSettingsFile(_config)))
            {
                shared.ChangeSettings(settings => settings.scope = _root);
                using (var panel = new WebGLTextureSettingsCopyPanel(() => shared, () => { }))
                {
                    panel.StartScan(); DrainWindow(panel);
                    panel.StartCopy(false);
                    panel.Pump();
                    var before = paths.Skip(20).Select(path => File.ReadAllBytes(path + ".meta")).ToArray();
                    var external = shared.Settings.Copy();
                    external.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(_root), lastKnownPath = _root });
                    File.WriteAllText(Path.Combine(_config, AssetCheckSettingsFile.FileName), JsonUtility.ToJson(external));
                    UnityEngine.TestTools.LogAssert.Expect(LogType.Exception,
                        new System.Text.RegularExpressions.Regex("IOException: 配置文件已被外部修改或移除"));
                    panel.Pump();
                    Assert.That(panel.Running, Is.False);
                    Assert.That(panel.Q<Label>("status").text, Does.Contain("重新加载配置"));
                    Assert.That(panel.Q<Button>("copy-all").enabledInHierarchy, Is.False);
                    for (var index = 20; index < 25; index++)
                        Assert.That(File.ReadAllBytes(paths[index] + ".meta"), Is.EqualTo(before[index - 20]));
                }
            }
        }

        private static void DrainWindow(WebGLTextureSettingsCopyPanel window)
        {
            for (var index = 0; index < 100 && window.Q<Button>("cancel").enabledInHierarchy; index++)
                window.Pump();
            Assert.That(window.Q<Button>("cancel").enabledInHierarchy, Is.False);
            Assert.That(window.Q<Label>("status").text, Does.Not.Contain("失败"));
        }

        private string Texture(string name)
        {
            var path = _root + "/" + name + ".png";
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true;
            importer.SaveAndReimport();
            return path;
        }

        private static TextureImporterPlatformSettings ConfigureWebGL(TextureImporter importer)
        {
            var source = importer.GetPlatformTextureSettings("WebGL");
            source.overridden = true;
            source.format = TextureImporterFormat.ASTC_8x8;
            source.maxTextureSize = 256;
            source.resizeAlgorithm = TextureResizeAlgorithm.Bilinear;
            source.textureCompression = TextureImporterCompression.CompressedHQ;
            source.compressionQuality = 87;
            source.crunchedCompression = true;
            source.allowsAlphaSplitting = true;
            source.ignorePlatformSupport = true;
            source.androidETC2FallbackOverride = AndroidETC2FallbackOverride.Quality16Bit;
            importer.SetPlatformTextureSettings(source);
            return importer.GetPlatformTextureSettings("WebGL");
        }

        private static void AssertTargets(TextureImporterPlatformSettings source, Func<string, TextureImporterPlatformSettings> read)
        {
            foreach (var target in new[] { "MiniGame", "WeixinMiniGame" })
            {
                var expected = new TextureImporterPlatformSettings();
                source.CopyTo(expected);
                expected.name = target;
                expected.overridden = true;
                Assert.That(JsonUtility.ToJson(read(target)), Is.EqualTo(JsonUtility.ToJson(expected)), target);
            }
        }
    }
}
