using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using July.Release.Editor;
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
    public sealed class BundleAnalysisComparisonTests
    {
        const string GuidA = "a1234567890123456789012345678901", GuidB = "b1234567890123456789012345678901";
        string temp;
        [SetUp] public void Setup() { temp = Path.GetFullPath("Temp/JulyComparison_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp); }
        [TearDown] public void Cleanup() { Assert.That(temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar)); Directory.Delete(temp, true); }
        static BundleReportAssetData Resource(string guid = GuidA, string path = "Assets/a.png") => new BundleReportAssetData { AssetPath = path, AssetGUID = guid };
        BundleInspectionSession Session(string name, params BundleReportBundleData[] bundles)
        {
            var path = Path.Combine(temp, name + ".report"); Write(path, bundles);
            return Restore(path);
        }
        static BundleInspectionSession Restore(string path)
        {
            var session = new BundleInspectionSession(BundleBuildReport.Read(path), AssetCheckSettings.CreateDefault(), new DateTime(2026, 11, 2, 12, 0, 0));
            foreach (var resource in session.Resources) { resource.Checked = true; resource.ContentChecked = true; resource.CheckedPath = resource.Asset.Path; }
            return session;
        }
        [Serializable] sealed class Sizes { public int schema = 1; public string reportHash; public SizeBundle[] bundles; }
        [Serializable] sealed class SizeBundle { public string name; public long unpackedBytes, unassignedBytes = 10; public SizeResource[] resources; public Generated[] generated = Array.Empty<Generated>(); }
        [Serializable] sealed class Generated { public string description; public long bytes; }
        [Serializable] sealed class SizeResource { public string guid, path, reason; public long bytes; public int coverage = 1; }
        static SizeResource Measured(string guid, long bytes, int coverage = 1) => new SizeResource { guid = guid, path = guid == GuidA ? "Assets/a.png" : "Assets/b.png", bytes = bytes, coverage = coverage, reason = coverage == 2 ? "共享数据单列" : null };
        static void Measure(BundleInspectionSession session, params (string Name, SizeResource[] Resources)[] measurements)
        {
            var path = session.Packages.Report.SourcePath;
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(path + ".sizes.json", JsonUtility.ToJson(new Sizes { reportHash = hash, bundles = measurements.Select(item =>
                new SizeBundle { name = item.Name, resources = item.Resources, unpackedBytes = item.Resources.Sum(resource => resource.bytes) + 10 }).ToArray() }));
        }

        [Test] public void PackageAddsRemovalsAndByteDeltasUseLogicalNamesRatherThanOutputHashes()
        {
            using (var a = Session("a", Bundle("Same", 10, Array.Empty<string>()), Bundle("Removed", 20, Array.Empty<string>())))
            using (var b = Session("b", Bundle("Same", 15, Array.Empty<string>()), Bundle("Added", 30, Array.Empty<string>())))
            {
                a.Packages.Report.Bundles["Same"].FileName = "oldhash.bundle"; b.Packages.Report.Bundles["Same"].FileName = "newhash.bundle";
                var comparison = new BundleAnalysisComparison(a, b);
                Assert.AreEqual(5, comparison.Packages.Single(row => row.Id == "Same").Delta);
                Assert.AreEqual(-20, comparison.Packages.Single(row => row.Id == "Removed").Delta);
                Assert.AreEqual(30, comparison.Packages.Single(row => row.Id == "Added").Delta);
                Assert.That(comparison.Packages.Single(row => row.Id == "Added").Change, Does.Contain("新增包"));
                Assert.AreEqual(15, b.Packages.Report.TotalBytes - a.Packages.Report.TotalBytes);
            }
        }

        [Test] public void SharedDependencyIsCountedOnceAndDependencyGrowthIsVisibleOnItsParents()
        {
            BundleReportBundleData[] Graph(long d) => new[] { Bundle("A", 10, new[] { "B", "C" }), Bundle("B", 20, new[] { "D" }), Bundle("C", 30, new[] { "D" }), Bundle("D", d, Array.Empty<string>()) };
            using (var a = Session("a", Graph(40))) using (var b = Session("b", Graph(80)))
            {
                var comparison = new BundleAnalysisComparison(a, b); var parent = comparison.Packages.Single(row => row.Id == "A");
                Assert.AreEqual(100, a.Packages.TotalBytes("A")); Assert.AreEqual(140, b.Packages.TotalBytes("A"));
                Assert.AreEqual(3, b.Packages.Dependencies("A").Length); Assert.AreEqual(0, parent.Delta);
                Assert.That(parent.Change, Does.Contain("含依赖总计变化").And.Not.Contain("报告依赖变化"));
            }
        }

        [Test] public void CycleAndReportedDependencyChangesAreDetectedWithoutInfiniteExpansion()
        {
            using (var a = Session("a", Bundle("A", 10, new[] { "B" }), Bundle("B", 20, Array.Empty<string>())))
            using (var b = Session("b", Bundle("A", 10, new[] { "B" }), Bundle("B", 20, new[] { "A" })))
            {
                var comparison = new BundleAnalysisComparison(a, b);
                Assert.IsTrue(comparison.Packages.All(row => row.Changes.Contains("循环状态变化")));
                Assert.That(comparison.Packages.Single(row => row.Id == "B").Change, Does.Contain("报告依赖变化"));
                Assert.AreEqual(30, b.Packages.TotalBytes("A"));
            }
        }

        [Test] public void GuidPreservesIdentityAcrossRenamesAndOwnerChanges()
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("B", 20, Array.Empty<string>(), Resource(GuidA.ToUpperInvariant(), "Assets/moved.png"))))
            {
                var comparison = new BundleAnalysisComparison(a, b); var row = comparison.Resources.Single();
                Assert.IsTrue(row.BeforeExists && row.AfterExists);
                Assert.That(row.Change, Does.Contain("路径变化").And.Contain("所属包变化").And.Not.Contain("新增入包"));
            }
        }

        [TestCase(true)] [TestCase(false)] public void DifferentOrMissingGuidNeverFallsBackToASamePath(bool differentGuid)
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("A", 10, Array.Empty<string>(), Resource(differentGuid ? GuidB : null))))
            {
                var rows = new BundleAnalysisComparison(a, b).Resources;
                Assert.AreEqual(2, rows.Length); Assert.AreEqual(1, rows.Count(row => !row.BeforeExists)); Assert.AreEqual(1, rows.Count(row => !row.AfterExists));
            }
        }

        [Test] public void PathIdentityStillMatchesWhenBothReportsHaveNoGuid()
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource(null))))
            using (var b = Session("b", Bundle("A", 10, Array.Empty<string>(), Resource(null))))
                Assert.IsFalse(new BundleAnalysisComparison(a, b).Resources.Single().Changed);
        }

        [Test] public void ResourceSizesSumRealCopiesAndDoNotUseWholeBundleSize()
        {
            using (var a = Session("a", Bundle("A", 9000, Array.Empty<string>(), Resource()), Bundle("B", 8000, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("A", 9000, Array.Empty<string>(), Resource()), Bundle("B", 8000, Array.Empty<string>(), Resource())))
            {
                Measure(a, ("A", new[] { Measured(GuidA, 10) }), ("B", new[] { Measured(GuidA, 20) }));
                Measure(b, ("A", new[] { Measured(GuidA, 15) }), ("B", new[] { Measured(GuidA, 25) }));
                using (var before = Restore(a.Packages.Report.SourcePath)) using (var after = Restore(b.Packages.Report.SourcePath))
                {
                    var row = new BundleAnalysisComparison(before, after).Resources.Single();
                    Assert.AreEqual(30, row.BeforeBytes); Assert.AreEqual(40, row.AfterBytes); Assert.AreEqual(10, row.Delta);
                }
            }
        }

        [TestCase(0)] [TestCase(2)] public void UnknownOrPartialSizesNeverProduceInventedNumericDeltas(int coverage)
        {
            using (var a = Session("a", Bundle("A", 100, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("A", 100, Array.Empty<string>(), Resource())))
            {
                if (coverage == 2) Measure(a, ("A", new[] { Measured(GuidA, 300, 2) }));
                Measure(b, ("A", new[] { Measured(GuidA, 100, coverage == 2 ? 2 : 1) }));
                using (var before = Restore(a.Packages.Report.SourcePath)) using (var after = Restore(b.Packages.Report.SourcePath))
                {
                    var comparison = new BundleAnalysisComparison(before, after); var row = comparison.Resources.Single();
                    Assert.IsNull(row.Delta); Assert.IsTrue(row.Changed); Assert.That(comparison.Notice, Does.Contain("不计算大小差值"));
                }
            }
        }

        [Test] public void ZeroIsACompleteMeasurementAndAbsenceIsDifferentFromUnknown()
        {
            using (var a = Session("a", Bundle("A", 100, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("A", 100, Array.Empty<string>(), Resource(GuidB, "Assets/b.png"))))
            {
                Measure(a, ("A", new[] { Measured(GuidA, 0) })); Measure(b, ("A", new[] { Measured(GuidB, 20) }));
                using (var before = Restore(a.Packages.Report.SourcePath)) using (var after = Restore(b.Packages.Report.SourcePath))
                {
                    var rows = new BundleAnalysisComparison(before, after).Resources;
                    Assert.AreEqual(0, rows.Single(row => !row.AfterExists).Delta);
                    Assert.AreEqual(20, rows.Single(row => !row.BeforeExists).Delta);
                    Assert.AreEqual("未统计", BundleComparisonView.SizeCaption(true, null, AssetBundleSizeCoverage.Unknown));
                    Assert.AreEqual("未收录", BundleComparisonView.SizeCaption(false, 0, AssetBundleSizeCoverage.Unknown));
                }
            }
        }

        static void Issue(BundleResourceResult resource, string actual, bool reverse = false)
        {
            var check = new BundleResourceCheck { Kind = AssetCheckKind.Texture, Complete = true };
            check.Issues.Add(new AssetCheckIssue(resource.Asset.Path, resource.Asset.Guid, AssetCheckKind.Texture, "fixture.rule", "格式不合规", actual, "0", false,
                localId: 9876543210123, targets: reverse ? new[] { "MiniGame", "WebGL" } : new[] { "WebGL", "MiniGame" }, location: "Root", propertyPath: "m_Field"));
            resource.Checks.Add(check);
        }

        [Test] public void CheckEvidenceIgnoresSourceRenameAndTargetOrderButRetainsActualValuesAndSubObjectIdentity()
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("A", 10, Array.Empty<string>(), Resource(GuidA, "Assets/renamed.png"))))
            {
                Issue(a.Resources.Single(), "1"); Issue(b.Resources.Single(), "1", true);
                Assert.That(new BundleAnalysisComparison(a, b).Resources.Single().Change, Does.Not.Contain("检查记录变化"));
                b.Resources.Single().Checks.Clear(); Issue(b.Resources.Single(), "2");
                var comparison = new BundleAnalysisComparison(a, b); var row = comparison.Resources.Single();
                Assert.That(row.Change, Does.Contain("检查记录变化"));
                Assert.That(string.Join("\n", comparison.Details(row)), Does.Contain("9876543210123").And.Contain("实际：1").And.Contain("实际：2"));
            }
        }

        [Test] public void StandardChangesAreExplicitAndAllowedFormatSetOrderIsNotAStandardChange()
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("A", 10, Array.Empty<string>(), Resource())))
            {
                b.Settings.CurrentStandards.texture.allowedFormats = b.Settings.CurrentStandards.texture.allowedFormats.Reverse().ToArray();
                Assert.IsTrue(new BundleAnalysisComparison(a, b).SameStandards);
                Issue(a.Resources.Single(), "1"); b.Settings.CurrentStandards.texture.allowSpriteMipMaps = true;
                var comparison = new BundleAnalysisComparison(a, b);
                Assert.IsFalse(comparison.SameStandards); Assert.That(comparison.Notice, Does.Contain("不能认定为已修复"));
                Assert.That(comparison.Resources.Single().Change, Does.Contain("检查记录变化"));
            }
        }

        [Test] public void TargetGroupingDoesNotCreateFalseChangesAndOnlyChangedTargetIsReported()
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("A", 10, Array.Empty<string>(), Resource())))
            {
                Issue(a.Resources.Single(), "1"); var original = a.Resources.Single().Checks.Single().Issues.Single();
                var check = new BundleResourceCheck { Kind = AssetCheckKind.Texture, Complete = true };
                check.Issues.Add(original.ForTargets(new[] { "WebGL" }, false, true));
                check.Issues.Add(original.ForTargets(new[] { "MiniGame" }, false, true)); b.Resources.Single().Checks.Add(check);
                Assert.IsFalse(new BundleAnalysisComparison(a, b).Resources.Single().Changed);
                check.Issues.RemoveAt(1);
                check.Issues.Add(new AssetCheckIssue(original.AssetPath, original.TargetId, original.Kind, original.RuleId,
                    original.Problem, "2", original.Expected, false, localId: original.LocalId, targets: new[] { "MiniGame" }, location: original.Location, propertyPath: original.PropertyPath));
                var comparison = new BundleAnalysisComparison(a, b);
                var changes = comparison.Details(comparison.Resources.Single()).Where(line => line.StartsWith("记录内容变化：")).ToArray();
                Assert.AreEqual(1, changes.Length); Assert.That(changes.Single(), Does.Contain("[MiniGame]").And.Not.Contain("[WebGL]"));
            }
        }

        [Test] public void AddedAndDisappearedCheckRecordsAreExplicitWithoutClaimingRepairs()
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("A", 10, Array.Empty<string>(), Resource())))
            {
                Issue(a.Resources.Single(), "1"); Issue(b.Resources.Single(), "2");
                var check = b.Resources.Single().Checks.Single(); check.Issues.Clear();
                check.Issues.Add(new AssetCheckIssue("Assets/a.png", GuidA, AssetCheckKind.Texture, "another.rule", "新问题", "2", "0", false));
                var comparison = new BundleAnalysisComparison(a, b); var details = string.Join("\n", comparison.Details(comparison.Resources.Single()));
                Assert.That(details, Does.Contain("新增记录：").And.Contain("不再出现的记录：").And.Not.Contain("已修复"));
            }
        }

        [Test] public void OrdinaryScopeAndExclusionsDoNotFilterComparisonFacts()
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("A", 10, Array.Empty<string>(), Resource(), Resource(GuidB, "Assets/b.png"))))
            {
                a.Settings.scope = "Assets/Other"; b.Settings.scope = "Assets/Other";
                b.Settings.ignoredAssets.Add(new IgnoredAsset { guid = GuidB, lastKnownPath = "Assets/b.png" });
                var comparison = new BundleAnalysisComparison(a, b);
                Assert.AreEqual(2, comparison.Resources.Length); Assert.IsTrue(comparison.Resources.Single(row => row.Id.Contains(GuidB)).Changed);
                Assert.IsTrue(comparison.SameStandards);
            }
        }

        [Test] public void MissingPhysicalCopyMeasurementMakesTheAggregateIncomparable()
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource()), Bundle("B", 20, Array.Empty<string>(), Resource())))
            using (var b = Session("b", Bundle("A", 10, Array.Empty<string>(), Resource()), Bundle("B", 20, Array.Empty<string>(), Resource())))
            {
                Measure(a, ("A", new[] { Measured(GuidA, 10) }), ("B", new[] { Measured(GuidA, 20) }));
                Measure(b, ("A", new[] { Measured(GuidA, 10) }), ("B", Array.Empty<SizeResource>()));
                using (var before = Restore(a.Packages.Report.SourcePath)) using (var after = Restore(b.Packages.Report.SourcePath))
                {
                    var row = new BundleAnalysisComparison(before, after).Resources.Single();
                    Assert.AreEqual(10, row.AfterBytes); Assert.AreEqual(AssetBundleSizeCoverage.Partial, row.AfterCoverage); Assert.IsNull(row.Delta);
                }
            }
        }

        [Test] public void CrossPlatformComparisonKeepsItsContextWarning()
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>()))) using (var b = Session("b", Bundle("A", 20, Array.Empty<string>())))
            {
                b.Packages.Report.Data.Summary.BuildTarget = (int)BuildTarget.StandaloneWindows64;
                Assert.That(new BundleAnalysisComparison(a, b).Notice, Does.Contain("平台或 Package 不同"));
            }
        }

        [Test] public void UnfinishedChecksCannotBeComparedAndIdenticalResultsHaveNoFalseChanges()
        {
            using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource())))
            using (var unfinished = new BundleInspectionSession(a.Packages.Report, a.Settings, () => false))
            {
                Assert.Throws<InvalidOperationException>(() => new BundleAnalysisComparison(a, unfinished));
                var comparison = new BundleAnalysisComparison(a, a);
                Assert.IsFalse(comparison.Packages.Any(row => row.Changed)); Assert.IsFalse(comparison.Resources.Any(row => row.Changed));
            }
        }

        [UnityTest] public IEnumerator HistorySelectionAndComparisonFitTheSharedWindowAndDoNotRescan()
        {
            var settingsDirectory = Path.Combine(temp, "Project"); Directory.CreateDirectory(settingsDirectory);
            using (var workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(settingsDirectory)))
            {
                var cache = new BundleAnalysisCache(settingsDirectory);
                using (var a = Session("a", Bundle("A", 10, Array.Empty<string>(), Resource())))
                using (var b = Session("b", Bundle("A", 20, new[] { "B" }, Resource()), Bundle("B", 30, Array.Empty<string>(), Resource(GuidB, "Assets/b.png"))))
                {
                    foreach (var report in new[] { a.Packages.Report, b.Packages.Report })
                        using (var inspection = new BundleInspectionSession(report, workspace.Settings, () => false)) { Finish(inspection); cache.Save(inspection, new BundleAnalysisViewState()); }
                }
                var host = ScriptableObject.CreateInstance<EditorWindow>(); BundleAnalysisPanel panel = null;
                try
                {
                    host.position = new Rect(0, 0, 1050, 600); host.Show(); host.rootVisualElement.AddToClassList("asset-check");
                    host.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.assetcheck/Editor/Views/AssetCheckWindow.uss"));
                    panel = new BundleAnalysisPanel(() => workspace, () => { }); host.rootVisualElement.Add(panel);
                    yield return null; yield return null;
                    Assert.IsFalse(panel.Running); Assert.AreEqual(2, panel.Q<PopupField<BundleAnalysisRecord>>("analysis-history").choices.Count);
                    foreach (var id in new[] { "analyze-report", "open-analysis-history", "show-analysis-comparison" })
                    { var control = panel.Q<Button>(id); Assert.Greater(control.worldBound.width, 20); Assert.LessOrEqual(control.worldBound.xMax, host.rootVisualElement.worldBound.xMax + 1); }
                    var open = panel.Q<Button>("show-analysis-comparison"); using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = open; open.SendEvent(submit); }
                    var comparison = panel.Q<BundleComparisonView>();
                    var records = cache.Records(); comparison.Q<PopupField<BundleAnalysisRecord>>("comparison-before").value = records.Single(record => record.reportName == "a.report");
                    comparison.Q<PopupField<BundleAnalysisRecord>>("comparison-after").value = records.Single(record => record.reportName == "b.report"); comparison.Compare();
                    yield return null;
                    Assert.IsNotNull(comparison.Comparison); Assert.AreEqual(2, comparison.Q<MultiColumnListView>("comparison-results").itemsSource.Count);
                    comparison.ShowResources(true); Assert.AreEqual(1, comparison.Q<MultiColumnListView>("comparison-results").itemsSource.Count);
                    comparison.Q<MultiColumnListView>("comparison-results").SetSelection(0); yield return null;
                    Assert.IsNotEmpty(comparison.Q<ScrollView>("comparison-details").Query<Label>().ToList());
                    Assert.LessOrEqual(comparison.Q<Button>("compare-reports").worldBound.xMax, host.rootVisualElement.worldBound.xMax + 1);
                    Assert.IsFalse(panel.Running); Assert.IsFalse(BundleAnalysisBuildQueue.Running);
                }
                finally { panel?.Dispose(); Object.DestroyImmediate(host); }
            }
        }
    }
}
