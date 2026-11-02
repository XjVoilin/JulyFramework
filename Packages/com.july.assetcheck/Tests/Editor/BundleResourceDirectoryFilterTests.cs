using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static July.AssetCheck.Tests.BundleAnalysisPanelTests;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class BundleResourceDirectoryPathTests
    {
        [TestCase(null, "")] [TestCase("", "")] [TestCase(" Assets/Game/ ", "Assets/Game")]
        [TestCase("Assets\\Game\\Sub\\", "Assets/Game/Sub")] [TestCase("Assets", "Assets")]
        [TestCase("Packages/example/Deleted", "Packages/example/Deleted")]
        public void DirectoryInputNormalizesWithoutRequiringExistingFolders(string input, string expected) =>
            Assert.AreEqual(expected, BundleResourceDirectoryFilter.Normalize(input));

        [TestCase("AssetsOther/Game")] [TestCase("/Assets/Game")] [TestCase("E:/Project/Assets/Game")]
        [TestCase("Assets/../Other")] [TestCase("Assets/./Game")] [TestCase("Assets//Game")]
        public void DirectoryInputRejectsNonProjectPathsAndAmbiguousSegments(string input) =>
            Assert.Throws<ArgumentException>(() => BundleResourceDirectoryFilter.Normalize(input));

        [TestCase("Assets/Game", "Assets/Game/a.png", true)]
        [TestCase("Assets/Game", "Assets/Game/Sub/a.png", true)]
        [TestCase("Assets/Game", "Assets/GameOther/a.png", false)]
        [TestCase("Assets/Game", "Assets/Game.png", false)]
        [TestCase("", "Assets/Any/a.png", true)]
        public void MatchingUsesAWholeDirectoryBoundary(string directory, string path, bool expected) =>
            Assert.AreEqual(expected, BundleResourceDirectoryFilter.Contains(directory, path));

    }

    public sealed class BundleResourceDirectoryFilterTests
    {
        string root, temp, report, wanted, sibling, top, nested, outside;
        AssetCheckWorkspace workspace;
        BundleAnalysisPanel panel;
        EditorWindow host;
        Object selection;

        [SetUp] public void Setup()
        {
            selection = Selection.activeObject;
            root = "Assets/__DirectoryFilter_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(root));
            AssetDatabase.CreateFolder(root, "Wanted"); AssetDatabase.CreateFolder(root + "/Wanted", "Sub"); AssetDatabase.CreateFolder(root, "WantedOther");
            wanted = root + "/Wanted"; sibling = root + "/WantedOther";
            top = Texture(wanted + "/top.png"); nested = Texture(wanted + "/Sub/nested.png"); outside = Texture(sibling + "/outside.png");
            var bytes = wanted + "/raw.bytes"; File.WriteAllText(bytes, "raw"); AssetDatabase.ImportAsset(bytes);
            var missing = new BundleReportAssetData { AssetPath = wanted + "/deleted.bytes", AssetGUID = Guid.NewGuid().ToString("N") };
            temp = Path.GetFullPath("Temp/DirectoryFilter_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp); report = Path.Combine(temp, "fixture.report");
            Write(report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(top), Asset(nested), Asset(outside), Asset(bytes), missing), Bundle("B", 200, Array.Empty<string>(), Asset(top)) });
            workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(temp));
            workspace.SetFoldersIgnored(new[] { wanted }, true); workspace.ChangeSettings(settings => settings.scope = sibling);
        }
        static string Texture(string path)
        {
            var texture = new Texture2D(8, 8); File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.Sprite; importer.mipmapEnabled = true; importer.SaveAndReimport(); return path;
        }
        [TearDown] public void Cleanup()
        {
            panel?.Dispose(); if (host != null) Object.DestroyImmediate(host); workspace.Dispose(); Selection.activeObject = selection; AssetDatabase.DeleteAsset(root);
            Assert.That(temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar)); Directory.Delete(temp, true);
        }
        void Open(bool import = true)
        {
            host = ScriptableObject.CreateInstance<EditorWindow>(); host.minSize = new Vector2(1050, 600); host.position = new Rect(0, 0, 1050, 600); host.Show();
            host.rootVisualElement.AddToClassList("asset-check");
            host.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.assetcheck/Editor/Views/AssetCheckWindow.uss"));
            panel = new BundleAnalysisPanel(() => workspace, () => { }); host.rootVisualElement.Add(panel);
            if (import) { panel.ImportReport(report); Finish(panel.Session); panel.ShowResources(true); }
        }
        void Close() { panel.Dispose(); panel = null; Object.DestroyImmediate(host); host = null; }
        static void Submit(Button button) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = button; button.SendEvent(e); } }
        AssetCheckResultsView Results => panel.Q<AssetCheckResultsView>("resource-results");

        [Test] public void FolderFiltersOnlyDisplayAndUpdatesTypeAndUnclassifiedCounts()
        {
            Open(); var session = panel.Session; var checkedAt = session.CheckedAt; var metadata = File.ReadAllBytes(top + ".meta");
            var settings = File.ReadAllBytes(Path.Combine(temp, AssetCheckSettingsFile.FileName));
            panel.SetResourceDirectory(wanted + "/");
            CollectionAssert.AreEquivalent(new[] { top, nested }, Results.Model.Files.Select(file => file.Path));
            Assert.That(panel.Q<Button>("bundle-tab-Texture").tooltip, Does.Contain("2 个适用资源"));
            Assert.That(panel.Q<Button>("bundle-tab-unclassified").text, Does.EndWith("2"));
            Assert.That(panel.Q<Label>("analysis-counts").text, Does.Contain("全报告 5").And.Contain("当前目录 4").And.Contain("当前显示 2"));
            panel.SelectResourceType(null); Assert.AreEqual(2, Results.Model.Files.Count);
            panel.SetResourceDirectory(sibling); Assert.AreEqual(0, Results.Model.Files.Count);
            Assert.AreSame(session, panel.Session); Assert.AreEqual(checkedAt, session.CheckedAt); Assert.IsFalse(panel.Running);
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(top + ".meta")); CollectionAssert.AreEqual(settings, File.ReadAllBytes(Path.Combine(temp, AssetCheckSettingsFile.FileName)));
            Assert.AreEqual(5, session.Resources.Length, "普通忽略名单和扫描范围不能缩减入包检查快照");
        }

        [Test] public void DirectoryCombinesWithOwnerTypeSearchStatusAndSizeSort()
        {
            Open(); panel.SetResourceDirectory(wanted);
            panel.Q<TextField>("analysis-search").value = "nested"; panel.Q<PopupField<string>>("analysis-filter").value = "违规";
            Results.RestoreView(AssetCheckSort.Size, true, null, Array.Empty<string>(), false);
            Assert.AreEqual(nested, Results.Model.Files.Single().Path);
            panel.Q<TextField>("analysis-search").value = "";
            panel.SetResourceDirectory(sibling); Assert.AreEqual(outside, Results.Model.Files.Single().Path); Assert.AreEqual(AssetCheckSort.Size, Results.Model.Sort);
            panel.ShowResources(false); Assert.AreEqual(DisplayStyle.None, panel.Q<VisualElement>("resource-directory-tools").style.display.value);
            panel.InspectPackage("B"); Submit(panel.Q<ScrollView>("analysis-inspector").Query<Button>().ToList().Single(button => button.text == "查看此包的资源检查结果"));
            Assert.IsEmpty(Results.Model.Files); panel.SetResourceDirectory(wanted); Assert.AreEqual(top, Results.Model.Files.Single().Path);
            Assert.That(panel.Q<Button>("bundle-tab-Texture").tooltip, Does.Contain("1 个适用资源"));
            Assert.That(panel.Q<Label>("analysis-counts").text, Does.Contain("所属包：B"));
        }

        [Test] public void SelectedFolderAndAllFoldersActionsUseTheSameDisplayFilter()
        {
            Open(); Assert.IsTrue(panel.Q<TextField>("resource-directory").isDelayed, "路径应在提交时应用，避免逐字输入被前缀校验拦截");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(wanted);
            Submit(panel.Q<Button>("resource-directory-selected"));
            Assert.AreEqual(wanted, panel.Q<TextField>("resource-directory").value); Assert.AreEqual(2, Results.Model.Files.Count);
            Assert.Throws<ArgumentException>(() => panel.SetResourceDirectory("E:/Outside"));
            Assert.AreEqual(wanted, panel.Q<TextField>("resource-directory").value); Assert.AreEqual(2, Results.Model.Files.Count);
            Submit(panel.Q<Button>("resource-directory-all")); Assert.AreEqual("", panel.Q<TextField>("resource-directory").value);
            Assert.AreEqual(3, Results.Model.Files.Count);
        }

        [Test] public void HistoryUsesRecordedPathsAfterMovesAndDeletedDirectories()
        {
            Open(); panel.SetResourceDirectory(wanted); var time = panel.Session.CheckedAt; Close();
            Assert.AreEqual("", AssetDatabase.MoveAsset(top, sibling + "/moved.png")); Open(false);
            Assert.AreEqual(time, panel.Session.CheckedAt); Assert.IsFalse(panel.Running); Assert.AreEqual(2, Results.Model.Files.Count);
            panel.SetResourceDirectory(sibling); CollectionAssert.AreEqual(new[] { outside }, Results.Model.Files.Select(file => file.Path));
            panel.SetResourceDirectory(wanted); Close(); AssetDatabase.DeleteAsset(wanted); Open(false);
            Assert.AreEqual(wanted, panel.Q<TextField>("resource-directory").value); Assert.AreEqual(2, Results.Model.Files.Count);
            Assert.IsTrue(panel.Session.IsSnapshot);
        }

        [Test] public void EveryReportKeepsItsOwnDirectoryAndOldViewsDefaultToAll()
        {
            Open(); panel.SetResourceDirectory(wanted); Close(); Open(false);
            var other = Path.Combine(temp, "other.report"); Write(other, new[] { Bundle("Other", 10, Array.Empty<string>(), Asset(outside)) });
            panel.ImportReport(other); Finish(panel.Session); panel.ShowResources(true);
            Assert.AreEqual("", panel.Q<TextField>("resource-directory").value);
            panel.SetResourceDirectory(sibling); Close(); Open(false); Assert.AreEqual(sibling, panel.Q<TextField>("resource-directory").value);
            var picker = panel.Q<PopupField<BundleAnalysisRecord>>("analysis-history"); picker.value = picker.choices.Single(record => record.reportName == "fixture.report");
            Submit(panel.Q<Button>("open-analysis-history")); Assert.AreEqual(wanted, panel.Q<TextField>("resource-directory").value);
            Close(); var cache = new BundleAnalysisCache(temp); var record = cache.Records().Single(item => item.reportName == "fixture.report");
            var text = File.ReadAllText(record.ViewPath); text = System.Text.RegularExpressions.Regex.Replace(text, "\\\"directory\\\"\\s*:\\s*\\\"[^\\\"]*\\\",?", ""); File.WriteAllText(record.ViewPath, text);
            var restored = cache.Load(record.Id); using (restored.Session) Assert.AreEqual("", restored.View.directory);
        }

        [UnityTest] public IEnumerator FolderControlsAndResourceFooterRemainInsideTheMinimumWindow()
        {
            Open(); panel.SetResourceDirectory(wanted); Close(); Open(false); yield return null; yield return null;
            foreach (var id in new[] { "resource-directory", "resource-directory-selected", "resource-directory-all", "analysis-status" })
            {
                var control = panel.Q<VisualElement>(id); Assert.Greater(control.worldBound.width, 20, id);
                Assert.LessOrEqual(control.worldBound.xMax, host.rootVisualElement.worldBound.xMax + 1, id);
                Assert.LessOrEqual(control.worldBound.yMax, host.rootVisualElement.worldBound.yMax + 1, id);
            }
        }
    }
}
