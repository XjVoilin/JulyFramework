using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using July.Release.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static July.AssetCheck.Tests.BundleAnalysisPanelTests;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class BundleResourceSizeTests
    {
        [Serializable] class SizeFile { public int schema = 1; public string reportHash; public SizeBundle[] bundles; }
        [Serializable] class SizeBundle { public string name; public long unpackedBytes, unassignedBytes; public SizeResource[] resources; public Generated[] generated = Array.Empty<Generated>(); }
        [Serializable] class SizeResource { public string guid, path, reason; public long bytes; public int coverage = 1; }
        [Serializable] class Generated { public string description; public long bytes; }
        string root, temp, report, a, b;
        AssetCheckWorkspace workspace;
        EditorWindow host;
        BundleAnalysisPanel panel;
        [SetUp] public void Setup()
        {
            root = "Assets/__JulySizePanel_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(root));
            temp = Path.GetFullPath("Temp/JulySizePanel_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp); report = Path.Combine(temp, "fixture.report");
            a = root + "/a.bytes"; b = root + "/b.bytes"; File.WriteAllText(a, "a"); File.WriteAllText(b, "b"); AssetDatabase.Refresh();
            Write(report, new[] { Bundle("A", 100, Array.Empty<string>(), Asset(a), Asset(b)), Bundle("B", 200, Array.Empty<string>(), Asset(a)) });
            workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(temp));
        }
        [TearDown] public void Cleanup()
        {
            panel?.Dispose(); if (host != null) Object.DestroyImmediate(host); workspace.Dispose(); AssetDatabase.DeleteAsset(root);
            Assert.That(temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar)); Directory.Delete(temp, true);
        }
        void Snapshot()
        {
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(report))).Replace("-", "").ToLowerInvariant();
            SizeResource Resource(string path, long bytes) => new SizeResource { guid = AssetDatabase.AssetPathToGUID(path), path = path, bytes = bytes };
            var file = new SizeFile { reportHash = hash, bundles = new[] {
                new SizeBundle { name = "A", unpackedBytes = 300, resources = new[] { Resource(a, 100), Resource(b, 200) } },
                new SizeBundle { name = "B", unpackedBytes = 300, resources = new[] { Resource(a, 300) } } } };
            File.WriteAllText(AssetBundleSizeSnapshot.FilePath(report), JsonUtility.ToJson(file));
        }
        void Open()
        {
            host = ScriptableObject.CreateInstance<EditorWindow>(); host.position = new Rect(0, 0, 1200, 700); host.Show();
            panel = new BundleAnalysisPanel(() => workspace, () => { }); host.rootVisualElement.Add(panel); panel.ImportReport(report); Finish(panel.Session); panel.ShowResources(true); panel.SelectResourceType(null);
        }
        [Test] public void SizeSortIsNumericKeepsFileGroupsAndDoesNotRestoreProblemPriority()
        {
            AssetCheckFileData File(string id, long? bytes, bool partial = false) => new AssetCheckFileData { Id = id, Path = "Assets/" + id, Complete = true, SizeBytes = bytes, SizePartial = partial };
            var big = File("big", 2048); var small = File("small-problem", 10); var zero = File("zero", 0); var partial = File("partial", 900000, true); var unknown = File("unknown", null); var unverified = File("unverified", 1000); unverified.Unverified = true;
            small.Issues = new[] { new AssetCheckIssue(small.Path, small.Id, AssetCheckKind.Model, "a", "a", "1", "0", true), new AssetCheckIssue(small.Path, small.Id, AssetCheckKind.Model, "b", "b", "1", "0", true) };
            var model = new AssetCheckResultsModel(); model.BindFiles(new object(), "size", 1, new[] { unknown, small, partial, zero, unverified, big }); model.Toggle(small.Id); model.SortBy(AssetCheckSort.Size);
            CollectionAssert.AreEqual(new[] { big.Id, unverified.Id, small.Id, zero.Id, partial.Id, unknown.Id }, model.Rows.Where(r => r.IsFile).Select(r => r.File.Id));
            Assert.AreEqual(2, model.Rows.Count(r => r.Issue != null)); Assert.IsFalse(model.Rows.Any(r => r.UnknownHeader));
            model.SortBy(AssetCheckSort.Size); Assert.AreEqual(zero.Id, model.Rows.First().File.Id); Assert.AreEqual(unknown.Id, model.Rows.Last().File.Id);
        }
        [Test] public void MissingSnapshotIsUnknownAndNeverBecomesZero()
        {
            Open(); Assert.IsNull(panel.Session.Packages.Report.Sizes);
            Assert.IsTrue(panel.Q<AssetCheckResultsView>("resource-results").Model.Files.All(f => !f.Data.SizeBytes.HasValue));
            Assert.IsTrue(panel.Session.Resources.All(r => panel.Session.Packages.ResourceSize(r.Asset).Coverage == AssetBundleSizeCoverage.Unknown));
        }
        [UnityTest] public IEnumerator SharedTableShowsSumAndPackageInspectorShowsEachPhysicalCopyInSizeOrder()
        {
            Snapshot(); Open(); yield return null; yield return null;
            var table = panel.Q<AssetCheckResultsView>("resource-results"); var column = table.Q<MultiColumnListView>().columns["size"];
            Assert.AreEqual("合计入包大小", column.title); Assert.AreEqual(400, table.Model.Files.Single(f => f.Path == a).Data.SizeBytes);
            var header = table.Q<Button>("sort-size"); Assert.NotNull(header);
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = header; header.SendEvent(evt); }
            Assert.AreEqual(a, table.Model.Rows.First().File.Path);
            panel.ShowResources(false); panel.InspectPackage("A");
            var contents = panel.Q<ScrollView>("analysis-inspector").Query<Foldout>().ToList().Single(f => f.text.StartsWith("包内资源"));
            var links = contents.Query<Button>("analysis-resource-locate").ToList(); Assert.That(links[0].text, Does.Contain("b.bytes").And.Contain("200 B")); Assert.That(links[1].text, Does.Contain("a.bytes").And.Contain("100 B"));
            var checks = contents.Query<Button>("analysis-resource-checks").ToList();
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = checks[0]; checks[0].SendEvent(evt); }
            Assert.AreEqual("本包大小", column.title); Assert.AreEqual(100, table.Model.Files.Single(f => f.Path == a).Data.SizeBytes);
            Assert.IsFalse(table.Query<Button>().ToList().Any(button => button.name == "fix" || button.name == "ignore"));
        }
        [UnityTest] public IEnumerator ResourceCheckActionScrollsToLastUnclassifiedResource()
        {
            var paths = Enumerable.Range(0, 30).Select(i => root + "/resource" + i.ToString("D2") + ".bytes").ToArray();
            foreach (var path in paths) File.WriteAllText(path, "fixture" + path); AssetDatabase.Refresh();
            Write(report, new[] { Bundle("A", 100, Array.Empty<string>(), paths.Select(Asset).ToArray()) });
            Open(); host.minSize = new Vector2(1050, 600); host.position = new Rect(0, 0, 1050, 600);
            host.rootVisualElement.AddToClassList("asset-check");
            host.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.assetcheck/Editor/Views/AssetCheckWindow.uss"));
            panel.ShowResources(false); panel.InspectPackage("A");
            var contents = panel.Q<ScrollView>("analysis-inspector").Query<Foldout>().ToList().Single(f => f.text.StartsWith("包内资源"));
            contents.value = true; yield return null; yield return null;
            var action = contents.Query<Button>("analysis-resource-checks").ToList().Last();
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = action; action.SendEvent(evt); }
            yield return null; yield return null; yield return null;
            var table = panel.Q<AssetCheckResultsView>("resource-results");
            Assert.AreEqual(30, table.Model.Files.Count); Assert.IsTrue(table.Model.ShowUnknown);
            var target = table.Query<Label>("name").ToList().Single(label => label.text == "resource29.bytes");
            var viewport = table.Q<MultiColumnListView>().Q<ScrollView>().contentViewport.worldBound;
            Assert.GreaterOrEqual(target.worldBound.yMin + 1, viewport.yMin);
            Assert.LessOrEqual(target.worldBound.yMax, viewport.yMax + 1);
        }
        [Test] public void IgnoresAndCurrentResourceChangesDoNotChangeReportSizeSnapshot()
        {
            Snapshot(); workspace.SetFoldersIgnored(new[] { root }, true); Open(); var session = panel.Session; var asset = session.Resources.Single(r => r.Asset.Path == a).Asset;
            Assert.AreEqual(2, session.Resources.Length); Assert.AreEqual(400, session.Packages.ResourceSize(asset).Bytes);
            File.WriteAllText(a, new string('x', 10000)); AssetDatabase.ImportAsset(a, ImportAssetOptions.ForceSynchronousImport); panel.SynchronizeSettings();
            Assert.AreSame(session, panel.Session); Assert.AreEqual(400, session.Packages.ResourceSize(asset).Bytes);
            Assert.IsEmpty(AssetDatabase.MoveAsset(a, root + "/moved.bytes")); Assert.AreEqual(400, session.Packages.ResourceSize(asset).Bytes);
        }
        [UnityTest] public IEnumerator ExpandedPackageContentsAndGeneratedDataNeverOverlap()
        {
            var extra = Enumerable.Range(0, 30).Select(i => root + "/resource" + i.ToString("D2") + ".bytes").ToArray();
            foreach (var path in extra) File.WriteAllText(path, "fixture"); AssetDatabase.Refresh();
            foreach (var count in new[] { 4, 32 })
            {
                Write(report, new[] { Bundle("A", 100, new[] { "B" }, new[] { a, b }.Concat(extra).Take(count).Select(Asset).ToArray()), Bundle("B", 200, new[] { "A" }, Asset(a)) });
                Snapshot();
                if (panel == null)
                {
                    Open(); host.position = new Rect(0, 0, 1050, 600);
                    host.rootVisualElement.AddToClassList("asset-check");
                    host.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.assetcheck/Editor/Views/AssetCheckWindow.uss"));
                }
                else { panel.ImportReport(report); Finish(panel.Session); }
                panel.ShowResources(false); panel.InspectPackage("A");
                var inspector = panel.Q<ScrollView>("analysis-inspector");
                foreach (var dark in new[] { true, false })
                {
                    host.rootVisualElement.EnableInClassList("theme-dark", dark); host.rootVisualElement.EnableInClassList("theme-light", !dark);
                    panel.EnableInClassList("analysis-dark", dark); panel.EnableInClassList("analysis-light", !dark);
                    var foldouts = inspector.Query<Foldout>().ToList();
                    foreach (var foldout in foldouts) foldout.value = true;
                    yield return null; yield return null;
                    AssertInspectorLayout(inspector);
                    var childCounts = foldouts.Select(f => f.childCount).ToArray();
                    var action = inspector.contentContainer.Children().OfType<Button>().Last();
                    inspector.ScrollTo(action); yield return null; yield return null;
                    Assert.LessOrEqual(action.worldBound.yMax, inspector.contentViewport.worldBound.yMax + 1, "末尾操作必须可通过滚动到达。");
                    foreach (var foldout in foldouts) foldout.value = false;
                    yield return null;
                    foreach (var foldout in foldouts) foldout.value = true;
                    yield return null; yield return null;
                    CollectionAssert.AreEqual(childCounts, foldouts.Select(f => f.childCount));
                    AssertInspectorLayout(inspector);
                }
                panel.InspectPackage("B");
                foreach (var foldout in inspector.Query<Foldout>().ToList()) foldout.value = true;
                yield return null; yield return null;
                Assert.AreEqual(1, inspector.Query<Foldout>().ToList().Single(f => f.text.StartsWith("包内资源")).Query<Button>("analysis-resource-locate").ToList().Count);
                AssertInspectorLayout(inspector);
            }
        }
        static void AssertInspectorLayout(ScrollView inspector)
        {
            var previousBottom = float.NegativeInfinity;
            foreach (var child in inspector.contentContainer.Children())
            {
                Assert.GreaterOrEqual(child.worldBound.yMin + 1, previousBottom, "相邻详情分组不能重叠。");
                previousBottom = child.worldBound.yMax;
            }
            foreach (var row in inspector.Query<VisualElement>(className: "analysis-resource-row").ToList())
            {
                var locate = row.Q<Button>("analysis-resource-locate"); var checks = row.Q<Button>("analysis-resource-checks");
                Assert.Greater(locate.worldBound.width, 25); Assert.Greater(checks.worldBound.width, 25);
                Assert.LessOrEqual(locate.worldBound.xMax, checks.worldBound.xMin + 1, "定位与检查操作不能重叠。");
                Assert.LessOrEqual(checks.worldBound.xMax, inspector.contentViewport.worldBound.xMax + 1, "检查按钮不能超出详情区。");
                Assert.GreaterOrEqual(row.worldBound.yMax + 1, Math.Max(locate.worldBound.yMax, checks.worldBound.yMax));
            }
            foreach (var foldout in inspector.Query<Foldout>().ToList().Where(f => f.value))
                foreach (var child in foldout.contentContainer.Children())
                    Assert.GreaterOrEqual(foldout.worldBound.yMax + 1, child.worldBound.yMax, "展开项必须容纳全部内容，不能溢出到下一节。");
        }
        [Test] public void DamagedSizeSnapshotDoesNotDiscardPreviouslyViewedSession()
        {
            Snapshot(); Open(); var previous = panel.Session;
            File.WriteAllText(AssetBundleSizeSnapshot.FilePath(report), "{broken"); Assert.Throws<InvalidDataException>(() => panel.ImportReport(report)); Assert.AreSame(previous, panel.Session);
        }
        [Test] public void ReportAndSizeSnapshotMustHaveSameBundleSet()
        {
            Snapshot(); var path = AssetBundleSizeSnapshot.FilePath(report); File.WriteAllText(path, File.ReadAllText(path).Replace("\"name\":\"B\"", "\"name\":\"C\""));
            Assert.Throws<InvalidDataException>(() => BundleBuildReport.Read(report));
        }
        [Test] public void BuildActionWaitsForCurrentResourceInspection()
        {
            host = ScriptableObject.CreateInstance<EditorWindow>(); panel = new BundleAnalysisPanel(() => workspace, () => { }); host.rootVisualElement.Add(panel); panel.ImportReport(report);
            Assert.IsFalse(panel.Q<Button>("build-analysis-bundles").enabledSelf); Finish(panel.Session); panel.SynchronizeSettings(); Assert.IsTrue(panel.Q<Button>("build-analysis-bundles").enabledSelf);
        }
    }
}
