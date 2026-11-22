using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class ResourceToolsWindowTests
    {
        private string _root;
        private byte[] _settingsBefore;
        private string _analysisCachePath, _analysisCacheBackup;
        private AssetCheckWindow _window;

        [SetUp]
        public void Setup()
        {
            _analysisCachePath = Path.GetFullPath(new BundleAnalysisCache(Directory.GetCurrentDirectory()).DirectoryPath);
            _analysisCacheBackup = Path.GetFullPath("Temp/BundleHistoryBackup_" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(_analysisCachePath)) Directory.Move(_analysisCachePath, _analysisCacheBackup);
            _settingsBefore = File.Exists(AssetCheckSettingsFile.FileName) ? File.ReadAllBytes(AssetCheckSettingsFile.FileName) : null;
            _root = "Assets/__ResourceTools_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
        }

        [TearDown]
        public void Cleanup()
        {
            if (_window != null) Object.DestroyImmediate(_window);
            AssetDatabase.DeleteAsset(_root);
            Assert.AreEqual(Path.GetFullPath("July/AssetCheck/BundleReports"), _analysisCachePath);
            if (Directory.Exists(_analysisCachePath)) Directory.Delete(_analysisCachePath, true);
            if (Directory.Exists(_analysisCacheBackup)) Directory.Move(_analysisCacheBackup, _analysisCachePath);
            if (_settingsBefore == null) File.Delete(AssetCheckSettingsFile.FileName);
            else File.WriteAllBytes(AssetCheckSettingsFile.FileName, _settingsBefore);
        }

        [UnityTest]
        public IEnumerator OneWindowSharesScopeAndRetainsIndependentResultsAcrossTools()
        {
            var texture = Texture("one");
            AssetDatabase.CreateFolder(_root, "empty");
            Open();
            var root = _window.rootVisualElement;
            var check = root.Q<AssetCheckResultsView>();
            var copy = root.Q<WebGLPlatformSettingsCopyPanel>();
            Assert.That(root.Query<ObjectField>().ToList().Count(field => field.objectType == typeof(DefaultAsset)), Is.EqualTo(1));
            Assert.That(AssetDatabase.GetAssetPath(root.Q<ObjectField>("shared-scope").value), Is.EqualTo(_root));
            Submit(root.Q<Button>("tab-Texture"));
            _window.CheckAll();
            for (var frame = 0; _window.FolderOperationRunning && frame < 1000; frame++) yield return null;
            Assert.That(_window.FolderOperationRunning, Is.False);
            Assert.That(check.Model.TotalAttentionFiles, Is.GreaterThan(0));
            var checkRows = check.Model.Rows;
            _window.SelectTool(AssetCheckWindow.ToolPage.PlatformCopy);
            copy.StartScan();
            Drain(copy);
            var copyRows = copy.Q<MultiColumnListView>("results").itemsSource;
            Assert.That(copyRows.Count, Is.EqualTo(1));
            _window.SelectTool(AssetCheckWindow.ToolPage.ResourceCheck);
            Assert.That(check.Model.Rows, Is.SameAs(checkRows));
            _window.SelectTool(AssetCheckWindow.ToolPage.PlatformCopy);
            Assert.That(copy.Q<MultiColumnListView>("results").itemsSource, Is.SameAs(copyRows));
            // 共享忽略名单更新后，复制页旧结果必须失效。
            _window.SetFoldersIgnored(new[] { _root }, true);
            Assert.That(copy.Q<Button>("copy-all").enabledInHierarchy, Is.False);
            Assert.That(copy.Q<MultiColumnListView>("results").itemsSource.Count, Is.Zero);
            copy.StartScan(); Drain(copy);
            Assert.That(copy.Q<Label>("counts").text, Does.Contain("忽略 1 个"));
            Assert.Throws<InvalidOperationException>(() => _window.SetScope(texture));
            _window.SetScope(_root + "/empty");
            Assert.That(copy.Q<MultiColumnListView>("results").itemsSource.Count, Is.Zero);
            Assert.That(copy.Q<Button>("copy-all").enabledInHierarchy, Is.False);
            Assert.That(root.Q<Button>("fix-all").enabledInHierarchy, Is.False);
            Assert.That(new AssetCheckSettingsFile(Directory.GetCurrentDirectory()).ReadExistingOrDefault().scope, Is.EqualTo(_root + "/empty"));
        }

        [Test]
        public void CopyLocksSharedControlsAndClosingStopsRemainingBatches()
        {
            var paths = Enumerable.Range(0, 25).Select(index => Texture(index.ToString("D2"))).ToArray();
            Open();
            _window.SelectTool(AssetCheckWindow.ToolPage.PlatformCopy);
            var root = _window.rootVisualElement;
            var copy = root.Q<WebGLPlatformSettingsCopyPanel>();
            copy.StartScan();
            Assert.That(root.Q<ObjectField>("shared-scope").enabledInHierarchy, Is.False);
            Assert.That(root.Q<Button>("tool-check").enabledInHierarchy, Is.False);
            Assert.Throws<InvalidOperationException>(() => _window.SelectTool(AssetCheckWindow.ToolPage.ResourceCheck));
            Assert.Throws<InvalidOperationException>(() => _window.SetScope("Assets"));
            Drain(copy);
            Assert.That(root.Q<Button>("tool-check").enabledInHierarchy, Is.True);
            copy.StartCopy(false);
            copy.Pump();
            Assert.That(paths.Count(path => !WebGLPlatformSettingsCopy.Inspect(path).NeedsCopy), Is.EqualTo(20));
            Object.DestroyImmediate(_window);
            _window = null;
            copy.Pump();
            Assert.That(copy.Running, Is.False);
            Assert.That(paths.Count(path => !WebGLPlatformSettingsCopy.Inspect(path).NeedsCopy), Is.EqualTo(20));
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(Texture("afterClose")), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator CheckingLocksCopyAndCopyInvalidatesCheckReport()
        {
            Texture("invalidation");
            Open();
            var root = _window.rootVisualElement;
            var copy = root.Q<WebGLPlatformSettingsCopyPanel>();
            Submit(root.Q<Button>("tab-Texture"));
            _window.CheckAll();
            Assert.That(copy.Q<Button>("scan").enabledInHierarchy, Is.False);
            Assert.That(root.Q<Button>("tool-copy").enabledInHierarchy, Is.False);
            for (var frame = 0; _window.FolderOperationRunning && frame < 1000; frame++) yield return null;
            Assert.That(_window.FolderOperationRunning, Is.False);
            Assert.That(root.Q<AssetCheckResultsView>().Model.CanModify, Is.True);
            _window.SelectTool(AssetCheckWindow.ToolPage.PlatformCopy);
            copy.StartScan(); Drain(copy);
            copy.StartCopy(false); Drain(copy);
            // 等待宿主将检查报告过期状态绑定回检查页。
            for (var frame = 0; root.Q<AssetCheckResultsView>().Model.CanModify && frame < 200; frame++) yield return null;
            _window.SelectTool(AssetCheckWindow.ToolPage.ResourceCheck);
            Assert.That(root.Q<AssetCheckResultsView>().Model.CanModify, Is.False);
            // Model 立即反映过期状态，按钮由宿主按时间刷新；等待按钮状态以避免批处理帧率干扰。
            for (var attempt = 0; root.Q<Button>("fix-all").enabledInHierarchy && attempt < 20; attempt++)
                yield return new WaitForSecondsRealtime(0.05f);
            Assert.That(root.Q<Button>("fix-all").enabledInHierarchy, Is.False);
        }

        [UnityTest]
        public IEnumerator FileAndFolderIgnoresApplyEquallyToCheckingAndCopying()
        {
            AssetDatabase.CreateFolder(_root, "Skip");
            AssetDatabase.CreateFolder(_root, "SkipOther");
            var single = Texture("single");
            var nested = Texture("Skip/nested");
            var included = Texture("SkipOther/included");
            var atlas = _root + "/Skip/atlas.spriteatlas";
            AssetDatabase.CreateAsset(new SpriteAtlas(), atlas);
            var ignoredPaths = new[] { single + ".meta", nested + ".meta", atlas, atlas + ".meta" };
            var before = ignoredPaths.Select(File.ReadAllBytes).ToArray();
            var settings = AssetCheckSettings.CreateDefault();
            settings.scope = _root;
            settings.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(single), lastKnownPath = single });
            settings.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(_root + "/Skip"), lastKnownPath = _root + "/Skip" });
            File.WriteAllText(AssetCheckSettingsFile.FileName, JsonUtility.ToJson(settings, true));
            Open();
            _window.CheckAll();
            for (var frame = 0; _window.FolderOperationRunning && frame < 1000; frame++) yield return null;
            var check = new AssetCheckReportStore(Directory.GetCurrentDirectory()).Load(AssetCheckKind.Texture);
            Assert.That(check.TotalFiles, Is.EqualTo(4));
            Assert.That(check.IgnoredFiles, Is.EqualTo(3));
            Assert.That(check.CheckedFiles, Is.EqualTo(1));
            _window.SelectTool(AssetCheckWindow.ToolPage.PlatformCopy);
            var copy = _window.rootVisualElement.Q<WebGLPlatformSettingsCopyPanel>();
            copy.StartScan(); Drain(copy);
            var rows = copy.Q<MultiColumnListView>("results").itemsSource;
            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(((WebGLPlatformSettingsCopyEntry)rows[0]).Path, Is.EqualTo(included));
            Assert.That(copy.Q<Label>("counts").text, Does.Contain("忽略 3 个"));
            copy.StartCopy(false); Drain(copy);
            Assert.That(WebGLPlatformSettingsCopy.Inspect(included).NeedsCopy, Is.False);
            for (var index = 0; index < ignoredPaths.Length; index++)
                Assert.That(File.ReadAllBytes(ignoredPaths[index]), Is.EqualTo(before[index]), ignoredPaths[index]);
        }

        [UnityTest]
        public IEnumerator ReportPageFitsWindowAndRetainsImportedSnapshotAcrossTools()
        {
            Texture("report-view");
            var assetPath = _root + "/report-view.png";
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var report = new BundleReportData
            {
                Summary = new BundleReportSummary
                {
                    YooVersion = "2.3.12", UnityVersion = "2022.3", BuildDate = "2026/10/30 12:00:00", BuildPackageName = "Test", BuildPackageVersion = "1",
                    BuildTarget = (int)BuildTarget.WebGL, AllBundleTotalCount = 1, AllBundleTotalSize = 100, MainAssetTotalCount = 1
                },
                BundleInfos = new[] { new BundleReportBundleData
                {
                    BundleName = "only", FileName = "only.bundle", FileSize = 100,
                    DependBundles = Array.Empty<string>(), BundleContents = new[] { new BundleReportAssetData { AssetPath = assetPath, AssetGUID = guid } }
                } },
                AssetInfos = new[] { new BundleReportAssetData { AssetPath = assetPath, AssetGUID = guid, MainBundleName = "only", DependBundles = Array.Empty<string>() } }
            };
            var path = Path.GetFullPath("Temp/" + Guid.NewGuid().ToString("N") + ".report");
            File.WriteAllText(path, JsonUtility.ToJson(report));
            try
            {
                Open();
                _window.position = new Rect(0, 0, 1050, 600);
                _window.SelectTool(AssetCheckWindow.ToolPage.BundleAnalysis);
                var panel = _window.rootVisualElement.Q<BundleAnalysisPanel>();
                panel.ImportReport(path);
                yield return null; yield return null;
                for (var frame = 0; panel.Running && frame < 1000; frame++) yield return null;
                Assert.That(panel.Running, Is.False);
                Assert.That(panel.Session.Complete, Is.True, panel.Session.Failure);
                var list = panel.Q<MultiColumnListView>("analysis-results");
                Assert.That(list.worldBound.height, Is.GreaterThanOrEqualTo(80));
                Assert.That(panel.Q<Label>("analysis-status").worldBound.yMax, Is.LessThanOrEqualTo(_window.rootVisualElement.worldBound.yMax + 1));
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                var session = panel.Session;
                _window.SelectTool(AssetCheckWindow.ToolPage.PlatformCopy);
                _window.SelectTool(AssetCheckWindow.ToolPage.BundleAnalysis);
                Assert.That(panel.Session, Is.SameAs(session));
                _window.SetFoldersIgnored(new[] { _root }, true);
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                Assert.That(panel.Session.Resources.Length, Is.EqualTo(1));
                Assert.That(panel.Session.Packages.Report.TotalBytes, Is.EqualTo(100));
                panel.ShowResources(true); panel.InspectResource(session.Resources.Single());
                yield return null; yield return null;
                Assert.That(panel.Q<ScrollView>("analysis-inspector").worldBound.width, Is.GreaterThanOrEqualTo(280));
                Assert.That(panel.Q<AssetCheckResultsView>("resource-results").worldBound.height, Is.GreaterThanOrEqualTo(80));
                Assert.That(panel.Q<Button>("analysis-check-directory"), Is.Null);
                Assert.That(_window.FolderOperationRunning, Is.False);
                Assert.That(_window.rootVisualElement.Q<ObjectField>("shared-scope").parent.style.display.value, Is.EqualTo(DisplayStyle.None));
                File.Delete(path);
                var sharedSettings = _window.rootVisualElement.Q<ToolbarMenu>("shared-settings");
                sharedSettings.menu.MenuItems().OfType<DropdownMenuAction>().Single(action => action.name == "重新加载配置").Execute();
                Assert.That(panel.Session, Is.SameAs(session));
                Assert.That(panel.Q<Button>("analyze-report").enabledInHierarchy, Is.True);
            }
            finally { File.Delete(path); }
        }

        private void Open()
        {
            _window = ScriptableObject.CreateInstance<AssetCheckWindow>();
            _window.Show();
            _window.CreateGUI();
            _window.SetScope(_root);
        }

        [UnityTest]
        public IEnumerator AudioStandardsUseLabeledFieldsAndFitTheForm()
        {
            Open();
            _window.position = new Rect(80, 80, 1050, 600);
            var root = _window.rootVisualElement;
            Submit(root.Q<Button>("tab-Audio"));
            var foldout = root.Q<Foldout>(className: "standards");
            foldout.SetValueWithoutNotify(true);
            for (var frame = 0; frame < 5; frame++) yield return null;
            var form = foldout.Q<ScrollView>();
            var content = form.Q<VisualElement>(className: "standard-fields");
            var fields = content.Query<EnumFlagsField>().ToList();
            Assert.That(fields.Count, Is.EqualTo(2));
            Assert.That(fields.All(field => field.ClassListContains("standard-field")), Is.True);
            Assert.That(fields.All(field => field.worldBound.xMax <= form.worldBound.xMax + 1), Is.True);
            Assert.That(form.worldBound.height, Is.EqualTo(content.worldBound.height).Within(3));
        }

        [UnityTest]
        public IEnumerator TextureStandardsFitTheFormAndSeparateTexturesFromAtlases()
        {
            Open();
            _window.position = new Rect(80, 80, 1050, 600);
            var root = _window.rootVisualElement;
            Submit(root.Q<Button>("tab-Texture"));
            var foldout = root.Q<Foldout>(className: "standards");
            foldout.SetValueWithoutNotify(true);
            for (var frame = 0; frame < 5; frame++) yield return null;
            var form = foldout.Q<ScrollView>();
            var content = form.Q<VisualElement>(className: "standard-fields");
            Assert.That(form.worldBound.height, Is.EqualTo(Mathf.Min(190, content.worldBound.height)).Within(3),
                $"standards viewport is {form.worldBound.height}px, form content is {content.worldBound.height}px");
            var groups = content.Query<VisualElement>(className: "format-standard").ToList();
            Assert.That(groups.Count, Is.EqualTo(2));
            Assert.That(groups[0].worldBound.yMin, Is.EqualTo(groups[1].worldBound.yMin).Within(1));
            Assert.That(groups[0].Query<PopupField<string>>().ToList().Select(field => field.label),
                Is.EquivalentTo(new[] { "Sprite MipMap", "贴图 Read/Write" }));
            Assert.That(groups[1].Query<PopupField<string>>().ToList().Select(field => field.label),
                Is.EquivalentTo(new[] { "图集 MipMap", "图集 Read/Write" }));
            var formats = content.Query<VisualElement>(className: "format-options").ToList()
                .SelectMany(options => options.Query<Toggle>().ToList()).ToArray();
            Assert.That(formats.Select(format => format.text),
                Is.EquivalentTo(MiniGameProfile.TextureFormats.Concat(MiniGameProfile.TextureFormats).Select(TextureFormatRule.FormatName)));

            _window.position = new Rect(80, 80, 700, 600);
            for (var frame = 0; frame < 5; frame++) yield return null;
            Assert.That(groups[1].worldBound.yMin, Is.GreaterThanOrEqualTo(groups[0].worldBound.yMax));
            Assert.That(form.worldBound.height, Is.EqualTo(190).Within(3));
            Assert.That(form.verticalScroller.highValue, Is.GreaterThan(0));
            Assert.That(root.Q<AssetCheckResultsView>().worldBound.height, Is.GreaterThanOrEqualTo(80));
        }

        private string Texture(string name)
        {
            var path = _root + "/" + name + ".png";
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        private static void Drain(WebGLPlatformSettingsCopyPanel panel)
        {
            for (var index = 0; panel.Running && index < 100; index++) panel.Pump();
            Assert.That(panel.Running, Is.False);
            Assert.That(panel.Q<Label>("status").text, Does.Not.Contain("失败"));
        }

        private static void Submit(Button button)
        {
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
        }
    }
}
