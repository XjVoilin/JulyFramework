using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class BundleAnalysisPanelTests
    {
        private string _root, _temp, _path, _asset;
        private AssetCheckWorkspace _workspace;
        private EditorWindow _host;
        private BundleAnalysisPanel _panel;
        internal static BundleReportAssetData Asset(string path) => new BundleReportAssetData { AssetPath = path, AssetGUID = AssetDatabase.AssetPathToGUID(path) };
        internal static BundleReportBundleData Bundle(string name, long size, string[] dependencies, params BundleReportAssetData[] assets) =>
            new BundleReportBundleData { BundleName = name, FileName = name + ".bundle", FileSize = size, DependBundles = dependencies, BundleContents = assets };
        internal static void Write(string path, BundleReportBundleData[] bundles, params BundleReportAssetData[] main)
        {
            File.WriteAllText(path, JsonUtility.ToJson(new BundleReportData { Summary = new BundleReportSummary { YooVersion = "2.3.12", UnityVersion = "2022.3", BuildDate = "2026/11/01", BuildPackageName = "Fixture", BuildPackageVersion = "1", BuildTarget = (int)BuildTarget.WebGL, AllBundleTotalCount = bundles.Length, AllBundleTotalSize = bundles.Sum(b => b.FileSize), MainAssetTotalCount = main.Length }, BundleInfos = bundles, AssetInfos = main }));
        }
        [SetUp] public void Setup()
        {
            _root = "Assets/__BundlePanel_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            _temp = Path.GetFullPath("Temp/BundlePanel_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_temp);
            _asset = _root + "/sprite.png"; var texture = new Texture2D(8, 8); File.WriteAllBytes(_asset, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(_asset, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(_asset); importer.textureType = TextureImporterType.Sprite; importer.mipmapEnabled = true; importer.SaveAndReimport();
            var source = Asset(_asset); source.MainBundleName = "A"; source.DependBundles = new[] { "B" }; source.DependAssets = new[] { new BundleReportDependencyData { AssetPath = _asset, AssetGUID = source.AssetGUID } };
            _path = Path.Combine(_temp, "fixture.report"); Write(_path, new[] { Bundle("A", 100, new[] { "B" }, source), Bundle("B", 200, new[] { "A" }, source) }, source);
            _workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_temp)); _workspace.ChangeSettings(s => s.scope = _root);
        }
        [TearDown] public void Cleanup()
        {
            _panel?.Dispose(); if (_host != null) Object.DestroyImmediate(_host); _workspace.Dispose(); AssetDatabase.DeleteAsset(_root);
            Assert.That(_temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar)); Directory.Delete(_temp, true);
        }
        private void Open()
        {
            _host = ScriptableObject.CreateInstance<EditorWindow>(); _host.position = new Rect(0, 0, 1100, 700); _host.Show();
            _panel = new BundleAnalysisPanel(() => _workspace, () => { }); _host.rootVisualElement.Add(_panel);
        }
        internal static void Finish(BundleInspectionSession session)
        {
            for (var i = 0; session.Running && i < 100000; i++) session.Step();
            Assert.That(session.Running, Is.False); Assert.That(session.Failure, Is.Null); Assert.That(session.Complete, Is.True);
        }
        [UnityTest] public IEnumerator TwoResultViewsCycleDetailsAndResourceStandardsAreReadOnly()
        {
            var before = File.ReadAllBytes(_asset + ".meta"); Open(); _panel.ImportReport(_path); Finish(_panel.Session); _panel.SynchronizeSettings();
            yield return null; yield return null;
            Assert.That(_panel.Q<MultiColumnListView>("analysis-results").itemsSource.Count, Is.EqualTo(2));
            Assert.That(_panel.Q<AssetCheckResultsView>("resource-results").style.display.value, Is.EqualTo(DisplayStyle.None));
            _panel.InspectPackage("A"); var cycle = _panel.Q<Foldout>("analysis-cycle"); Assert.That(cycle.value, Is.False); cycle.value = true;
            Assert.That(cycle.Q<Label>("cycle-path").text, Is.EqualTo("A → B → A"));
            Assert.That(cycle.Query<Label>().ToList().Any(l => l.text.Contains("报告资源依赖（可能间接）")), Is.True);
            var packages = _panel.Q<MultiColumnListView>("analysis-results");
            var sort = packages.Query<Button>().ToList().First(button => button.text == "自身大小 ↕");
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = sort; sort.SendEvent(evt); }
            Assert.That(packages.selectedItem, Is.EqualTo("A"), "排序不能把当前选择转移到其他包");
            _panel.ShowResources(true); var result = _panel.Session.Resources.Single(); _panel.InspectResource(result);
            Assert.That(result.Issues.Any(i => i.RuleId == "texture.sprite-mipmaps"), Is.True);
            Assert.That(result.Asset.Owners.Count, Is.EqualTo(2));
            Assert.That(_panel.Query<Button>().ToList().Any(b => b.text.Contains("修改全部") || b.text.Contains("修改选中")), Is.False);
            Assert.That(File.ReadAllBytes(_asset + ".meta"), Is.EqualTo(before));
        }
        static void Click(Button button)
        {
            Assert.That(button, Is.Not.Null);
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
        }
        [Test] public void PackageResourceNameLocatesMovedAssetWithoutLeavingPackageView()
        {
            var previousSelection = Selection.activeObject;
            try
            {
                Open(); _panel.ImportReport(_path); Finish(_panel.Session); _panel.SynchronizeSettings();
                var session = _panel.Session; var moved = _root + "/moved.png";
                Assert.That(AssetDatabase.MoveAsset(_asset, moved), Is.Empty);
                var before = File.ReadAllBytes(moved + ".meta"); _panel.InspectPackage("A");
                Click(_panel.Q<Button>("analysis-resource-locate"));
                Assert.That(Selection.activeObject, Is.SameAs(AssetDatabase.LoadMainAssetAtPath(moved)));
                Assert.That(_panel.Q<MultiColumnListView>("analysis-results").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(_panel.Q<MultiColumnListView>("analysis-results").selectedItem, Is.EqualTo("A"));
                Assert.That(_panel.Q<AssetCheckResultsView>("resource-results").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(_panel.Q<Label>("analysis-status").text, Is.EqualTo("已定位：" + moved));
                Assert.That(_panel.Session, Is.SameAs(session)); Assert.That(File.ReadAllBytes(moved + ".meta"), Is.EqualTo(before));
            }
            finally { Selection.activeObject = previousSelection; }
        }
        [Test] public void PackageResourceNameDoesNotLocateReplacementWithDifferentGuid()
        {
            var previousSelection = Selection.activeObject;
            try
            {
                Open(); _panel.ImportReport(_path); Finish(_panel.Session); _panel.SynchronizeSettings();
                AssetDatabase.DeleteAsset(_asset); var replacement = _root + "/replacement.png";
                var texture = new Texture2D(8, 8); File.WriteAllBytes(replacement, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(replacement, ImportAssetOptions.ForceSynchronousImport);
                Assert.That(AssetDatabase.MoveAsset(replacement, _asset), Is.Empty);
                var sentinel = AssetDatabase.LoadMainAssetAtPath(_root); Selection.activeObject = sentinel;
                _panel.InspectPackage("A"); Click(_panel.Q<Button>("analysis-resource-locate"));
                Assert.That(Selection.activeObject, Is.SameAs(sentinel));
                Assert.That(_panel.Q<Label>("analysis-status").text, Does.StartWith("当前工程未匹配，无法定位："));
                Assert.That(_panel.Q<MultiColumnListView>("analysis-results").selectedItem, Is.EqualTo("A"));
                Click(_panel.Q<Button>("analysis-resource-checks"));
                Assert.That(_panel.Q<AssetCheckResultsView>("resource-results").Model.Files.Single().Path, Is.EqualTo(_asset));
            }
            finally { Selection.activeObject = previousSelection; }
        }
        [Test] public void ExplicitResourceChecksActionRevealsSnapshotDespitePreviousFilters()
        {
            Open(); _panel.ImportReport(_path); Finish(_panel.Session); _panel.SynchronizeSettings();
            var session = _panel.Session; var result = session.Resources.Single(); var table = _panel.Q<AssetCheckResultsView>("resource-results");
            _panel.ShowResources(true); _panel.SetResourceDirectory("Assets/SomeOtherDirectory");
            _panel.Q<TextField>("analysis-search").value = "not-this-resource"; _panel.Q<PopupField<string>>("analysis-filter").value = "通过";
            _panel.ShowResources(false); _panel.InspectPackage("A");
            table.Model.Filter("not-this-rule"); Click(_panel.Q<Button>("analysis-resource-checks"));
            Assert.That(table.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(table.Model.Files.Single().Path, Is.EqualTo(_asset)); Assert.That(table.Model.Rule, Is.Null);
            Assert.That(table.Model.Expanded, Does.Contain(BundlePackageAnalysis.Identity(result.Asset)));
            Assert.That(table.Model.ShowUnknown, Is.True);
            Assert.That(_panel.Q<TextField>("resource-directory").value, Is.Empty);
            Assert.That(_panel.Q<TextField>("analysis-search").value, Is.Empty);
            Assert.That(_panel.Q<PopupField<string>>("analysis-filter").value, Is.EqualTo("全部"));
            Assert.That(_panel.Q<Label>("analysis-counts").text, Does.Contain("所属包：A"));
            Assert.That(_panel.Q<ScrollView>("analysis-inspector").Query<Label>().ToList().Any(label => label.text == _asset), Is.True);
            Assert.That(_panel.Session, Is.SameAs(session)); Assert.That(_workspace.GetReport(AssetCheckKind.Texture), Is.Null);
        }
        [Test] public void CompletedSnapshotSurvivesScopeIgnoreResourceChangesAndExternalConfiguration()
        {
            Open(); _panel.ImportReport(_path); Finish(_panel.Session); var session = _panel.Session; var issues = session.Resources.Single().Issues.Count;
            _workspace.SetFoldersIgnored(new[] { _root }, true); _workspace.ChangeSettings(s => s.scope = "Assets"); _panel.SynchronizeSettings();
            AssetDatabase.DeleteAsset(_asset); _panel.SynchronizeSettings();
            Assert.That(_panel.Session, Is.SameAs(session)); Assert.That(session.Resources.Single().Issues.Count, Is.EqualTo(issues));
            Assert.That(session.Packages.Report.TotalBytes, Is.EqualTo(300)); Assert.That(session.Packages.Cyclic("A"), Is.True);
            File.WriteAllText(Path.Combine(_temp, AssetCheckSettingsFile.FileName), "{broken"); _workspace.PollExternalChanges(); _panel.SynchronizeSettings();
            Assert.That(_panel.Session, Is.SameAs(session)); Assert.That(_panel.Q<Button>("analyze-report").enabledSelf, Is.False);
        }
        [Test] public void ReportWithoutGuidExplicitlyLabelsPathMatchingInSnapshot()
        {
            var source = Asset(_asset); source.AssetGUID = null; source.MainBundleName = "A";
            Write(_path, new[] { Bundle("A", 100, Array.Empty<string>(), source) }, source);
            Open(); _panel.ImportReport(_path); Finish(_panel.Session);
            var result = _panel.Session.Resources.Single(); Assert.That(result.CheckedPath, Is.EqualTo(_asset));
            AssetDatabase.DeleteAsset(_asset); _panel.InspectResource(result);
            Assert.That(_panel.Q<ScrollView>("analysis-inspector").Query<Label>().ToList().Any(label => label.text == "检查时匹配：路径（报告无 GUID）"), Is.True);
        }
        [Test] public void CancelAndNewReportReplaceOnlyAnalysisState()
        {
            Open(); _panel.ImportReport(_path); _panel.Session.Step(); _panel.Session.Cancel();
            Assert.That(_panel.Session.Cancelled, Is.True); Assert.That(_panel.Session.Complete, Is.False); Assert.That(_panel.Session.Resources.Single().Status, Is.EqualTo("未完成"));
            Write(_path, new[] { Bundle("new", 400, Array.Empty<string>()) }); _panel.ImportReport(_path); Finish(_panel.Session);
            Assert.That(_panel.Session.Packages.Report.TotalBytes, Is.EqualTo(400)); Assert.That(_panel.Session.Resources, Is.Empty);
            Assert.That(_workspace.GetReport(AssetCheckKind.Texture), Is.Null);
        }
        [UnityTest] public IEnumerator ToolbarAndCancelActionsRemainInsideWindowWhileRunning()
        {
            Open(); _host.position = new Rect(0, 0, 1050, 600); _panel.ImportReport(_path);
            yield return null; yield return null;
            Assert.That(_panel.Running, Is.True);
            foreach (var element in new VisualElement[] { _panel.Q<Button>("analyze-report"), _panel.Q<Button>("analysis-cancel"), _panel.Q<PopupField<string>>("analysis-filter") })
            {
                Assert.That(element.worldBound.width, Is.GreaterThan(25), element.name);
                Assert.That(element.worldBound.xMin, Is.GreaterThanOrEqualTo(_host.rootVisualElement.worldBound.xMin), element.name);
                Assert.That(element.worldBound.xMax, Is.LessThanOrEqualTo(_host.rootVisualElement.worldBound.xMax + 1), element.name);
                Assert.That(element.worldBound.yMax, Is.LessThanOrEqualTo(_host.rootVisualElement.worldBound.yMax + 1), element.name);
            }
            Assert.That(_panel.Q<TextField>("bundle-report-path").worldBound.width, Is.GreaterThan(100));
            _panel.Session.Cancel(); Assert.That(_panel.Running, Is.False);
        }
    }
}
