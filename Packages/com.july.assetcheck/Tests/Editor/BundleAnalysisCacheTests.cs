using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    public sealed class BundleAnalysisCacheTests
    {
        string root, temp, report, texture, first, second;
        AssetCheckWorkspace workspace;
        BundleAnalysisCache cache;
        BundleAnalysisPanel panel;
        EditorWindow host;
        string DataFile => cache.Records().Single().DataPath;

        [SetUp] public void Setup()
        {
            root = "Assets/__BundleCache_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(root));
            temp = Path.GetFullPath("Temp/BundleCache_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            report = Path.Combine(temp, "fixture.report"); texture = root + "/sprite.png";
            var image = new Texture2D(8, 8); File.WriteAllBytes(texture, image.EncodeToPNG()); Object.DestroyImmediate(image);
            first = root + "/a.bytes"; second = root + "/b.bytes";
            File.WriteAllText(first, "same content"); File.WriteAllText(second, "same content"); AssetDatabase.Refresh();
            var importer = (TextureImporter)AssetImporter.GetAtPath(texture); importer.textureType = TextureImporterType.Sprite; importer.mipmapEnabled = true; importer.SaveAndReimport();
            var missing = new BundleReportAssetData { AssetPath = root + "/missing.png", AssetGUID = Guid.NewGuid().ToString("N") };
            Write(report, new[] { Bundle("A", 100, new[] { "B" }, Asset(texture), Asset(first), Asset(second), missing), Bundle("B", 200, new[] { "A" }, Asset(texture)) });
            workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(temp)); cache = new BundleAnalysisCache(temp);
        }

        [TearDown] public void Cleanup()
        {
            panel?.Dispose(); if (host != null) Object.DestroyImmediate(host);
            workspace.Dispose(); AssetDatabase.DeleteAsset(root);
            Assert.That(temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar)); Directory.Delete(temp, true);
        }

        BundleInspectionSession Inspect()
        {
            var session = new BundleInspectionSession(BundleBuildReport.Read(report), workspace.Settings, () => false);
            Finish(session); return session;
        }

        void Open()
        {
            host = ScriptableObject.CreateInstance<EditorWindow>(); host.position = new Rect(0, 0, 1100, 700); host.Show();
            panel = new BundleAnalysisPanel(() => workspace, () => { }); host.rootVisualElement.Add(panel);
        }

        void Reopen()
        {
            panel.Dispose(); panel = null; Object.DestroyImmediate(host); host = null;
            workspace.Dispose(); workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(temp)); Open();
        }

        static string Evidence(BundleInspectionSession session) => JsonUtility.ToJson(new EvidenceData
        {
            resources = session.Resources.Select(resource => new BundleCachedResource
            {
                id = BundlePackageAnalysis.Identity(resource.Asset), checkedPath = resource.CheckedPath,
                notes = resource.Notes.ToArray(), sameContent = resource.SameContent,
                checks = resource.Checks.Select(check => new BundleCachedCheck
                { kind = check.Kind, issues = check.Issues.Select(AssetCheckIssueSnapshot.Capture).ToArray() }).ToArray()
            }).ToArray()
        });
        [Serializable] sealed class EvidenceData { public BundleCachedResource[] resources; }

        [Test] public void MissingCacheCreatesNothingAndStartsNoInspection()
        {
            Assert.AreEqual(Path.Combine(temp, "July", "AssetCheck", "BundleReports"), Path.GetFullPath(cache.DirectoryPath));
            Assert.IsNull(cache.Load().Session); Assert.IsFalse(Directory.Exists(cache.DirectoryPath));
            Open(); Assert.IsNull(panel.Session); Assert.IsFalse(panel.Running);
        }

        [Test] public void CompleteSnapshotRestoresIssuesDuplicatesMissingAssetsAndOriginalStandardsWithoutRescanning()
        {
            using (var session = Inspect())
            {
                var evidence = Evidence(session); var time = session.CheckedAt;
                Assert.IsTrue(session.Resources.Any(resource => resource.Problem));
                Assert.AreEqual(2, session.Resources.Single(resource => resource.Asset.Path == first).SameContent.Length);
                cache.Save(session, new BundleAnalysisViewState());
                workspace.ChangeSettings(settings => settings.CurrentStandards.texture.allowSpriteMipMaps = true);
                AssetDatabase.DeleteAsset(texture);
                using (var restored = cache.Load().Session)
                {
                    Assert.IsTrue(restored.IsSnapshot); Assert.IsTrue(restored.Complete); Assert.IsFalse(restored.Running);
                    Assert.IsTrue(restored.ClassificationReady); Assert.AreEqual(time, restored.CheckedAt);
                    Assert.IsFalse(restored.Settings.CurrentStandards.texture.allowSpriteMipMaps);
                    Assert.AreEqual(evidence, Evidence(restored));
                    restored.Step(); Assert.AreEqual(evidence, Evidence(restored));
                    Assert.IsTrue(restored.Resources.All(resource => resource.Checked && resource.ContentChecked && resource.Checks.All(check => check.Complete)));
                    Assert.IsTrue(restored.Resources.Single(resource => resource.Asset.Path.EndsWith("missing.png")).Unverified);
                }
            }
        }

        [TestCase("running")] [TestCase("cancelled")] [TestCase("failed")]
        public void IncompleteInspectionsCannotReplaceThePreviousCompleteCache(string state)
        {
            using (var completed = Inspect()) cache.Save(completed, new BundleAnalysisViewState());
            var before = File.ReadAllBytes(DataFile);
            using (var session = new BundleInspectionSession(BundleBuildReport.Read(report), workspace.Settings, () => state == "failed"))
            {
                if (state == "cancelled") session.Cancel(); else if (state == "failed") session.Step();
                Assert.Throws<InvalidOperationException>(() => cache.Save(session, new BundleAnalysisViewState()));
            }
            Assert.AreEqual(before, File.ReadAllBytes(DataFile));
        }

        [TestCase("report changed")] [TestCase("report deleted")] [TestCase("sizes added")]
        [TestCase("sizes changed")] [TestCase("sizes deleted")]
        public void ArchiveSurvivesChangedOrMissingOriginalBuildFilesWithoutRescanning(string change)
        {
            if (change == "sizes changed" || change == "sizes deleted") WriteSizes();
            using (var session = Inspect()) cache.Save(session, new BundleAnalysisViewState());
            var before = File.ReadAllBytes(DataFile);
            if (change == "report changed") File.AppendAllText(report, " ");
            else if (change == "report deleted") File.Delete(report);
            else if (change == "sizes added") WriteSizes();
            else if (change == "sizes changed") File.AppendAllText(report + ".sizes.json", " ");
            else File.Delete(report + ".sizes.json");
            Open(); Assert.IsNotNull(panel.Session); Assert.IsFalse(panel.Running);
            Assert.IsTrue(panel.Session.IsSnapshot);
            Assert.That(panel.Q<Label>("report-source").text, Does.Contain("历史完整检查快照"));
            Assert.AreEqual(before, File.ReadAllBytes(DataFile));
        }

        void WriteSizes()
        {
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(report))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(report + ".sizes.json", "{\"schema\":1,\"reportHash\":\"" + hash + "\",\"bundles\":[" +
                "{\"name\":\"A\",\"unpackedBytes\":100,\"unassignedBytes\":100,\"resources\":[],\"generated\":[]}," +
                "{\"name\":\"B\",\"unpackedBytes\":200,\"unassignedBytes\":200,\"resources\":[],\"generated\":[]}]}");
        }

        [TestCase("json")] [TestCase("version")] [TestCase("resources")] [TestCase("checks")] [TestCase("issue")] [TestCase("duplicates")] [TestCase("view")]
        public void CorruptCacheIsReportedAndLeavesThePanelUsable(string failure)
        {
            using (var session = Inspect()) cache.Save(session, new BundleAnalysisViewState());
            var data = JsonUtility.FromJson<BundleAnalysisCacheData>(File.ReadAllText(DataFile));
            if (failure == "version") data.version = 99;
            else if (failure == "resources") data.resources[0] = null;
            else if (failure == "checks") data.resources.First(resource => resource.checks.Length > 0).checks[0].kind = (AssetCheckKind)99;
            else if (failure == "issue") data.resources.SelectMany(resource => resource.checks).First(check => check.issues.Length > 0).issues[0].localId = "bad";
            else if (failure == "duplicates") data.resources[0].sameContent = new[] { "Assets/missing.bytes" };
            else if (failure == "view") File.WriteAllText(cache.Records().Single().ViewPath, "{\"kind\":99}");
            File.WriteAllText(DataFile, failure == "json" ? "{broken" : JsonUtility.ToJson(data));
            Assert.Throws<InvalidDataException>(() => cache.Load());
            Open(); Assert.IsNull(panel.Session); Assert.IsTrue(panel.Q<Button>("analyze-report").enabledSelf);
            Assert.That(panel.Q<Label>("analysis-status").text, Does.Contain("历史分析未恢复"));
        }

        [UnityTest] public IEnumerator ReopeningRestoresSelectedPackageFiltersSortingAndExpandedDetails()
        {
            Open(); panel.ImportReport(report); Finish(panel.Session);
            panel.Q<TextField>("analysis-search").value = "A";
            panel.Q<PopupField<string>>("analysis-filter").value = "过小";
            var sort = panel.Q<MultiColumnListView>("analysis-results").Query<Button>().ToList().First(button => button.text == "依赖包数 ↕");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = sort; sort.SendEvent(submit); }
            panel.InspectPackage("A"); panel.Q<Foldout>("analysis-cycle").value = true;
            yield return null;
            var time = panel.Session.CheckedAt; Reopen(); yield return null;
            Assert.IsFalse(panel.Running); Assert.AreEqual(time, panel.Session.CheckedAt); Assert.IsTrue(panel.Session.IsSnapshot);
            Assert.AreEqual("A", panel.Q<TextField>("analysis-search").value);
            Assert.AreEqual("过小", panel.Q<PopupField<string>>("analysis-filter").value);
            Assert.AreEqual("A", panel.Q<MultiColumnListView>("analysis-results").selectedItem);
            Assert.IsTrue(panel.Q<Foldout>("analysis-cycle").value);
            Assert.That(panel.Q<Label>("report-source").text, Does.Contain("历史完整检查快照"));
            var saved = JsonUtility.FromJson<BundleAnalysisViewState>(File.ReadAllText(cache.Records().Single().ViewPath));
            Assert.AreEqual("deps", saved.sort);
        }

        [Test] public void ResourceViewStateAndHistoricalResultsSurviveResourceChangesAndExplicitAnalysisReplacesThem()
        {
            Open(); panel.ImportReport(report); Finish(panel.Session); panel.ShowResources(true);
            panel.SelectResourceType(AssetCheckKind.Texture);
            var results = panel.Q<AssetCheckResultsView>("resource-results");
            results.RestoreView(AssetCheckSort.Size, true, "texture.sprite-mipmaps", new[] { BundlePackageAnalysis.Identity(panel.Session.Resources.Single(resource => resource.Asset.Path == texture).Asset) }, true);
            panel.Q<TextField>("analysis-search").value = "sprite";
            panel.Q<PopupField<string>>("analysis-filter").value = "违规";
            var selected = panel.Session.Resources.Single(resource => resource.Asset.Path == texture); panel.InspectResource(selected);
            var evidence = Evidence(panel.Session); var time = panel.Session.CheckedAt;
            var importer = (TextureImporter)AssetImporter.GetAtPath(texture); importer.mipmapEnabled = false; importer.SaveAndReimport();
            Reopen();
            Assert.AreEqual(evidence, Evidence(panel.Session)); Assert.AreEqual(time, panel.Session.CheckedAt); Assert.IsFalse(panel.Running);
            results = panel.Q<AssetCheckResultsView>("resource-results");
            Assert.AreEqual(DisplayStyle.Flex, results.style.display.value);
            Assert.AreEqual(AssetCheckSort.Size, results.Model.Sort); Assert.IsTrue(results.Model.Descending);
            Assert.AreEqual("texture.sprite-mipmaps", results.Model.Rule); Assert.IsTrue(results.Model.ShowUnknown);
            Assert.AreEqual(1, results.Model.Expanded.Count);
            Assert.AreEqual("违规", panel.Q<PopupField<string>>("analysis-filter").value);
            Assert.That(panel.Q<ScrollView>("analysis-inspector").Query<Label>().ToList().Any(label => label.text == texture), Is.True);
            var recheck = panel.Q<Button>("recheck-analysis-history");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = recheck; recheck.SendEvent(submit); }
            Finish(panel.Session); panel.Pump(); panel.Dispose(); panel = null;
            using (var restored = cache.Load().Session)
                Assert.IsFalse(restored.Resources.SelectMany(resource => resource.Issues).Any(issue => issue.RuleId == "texture.sprite-mipmaps"));
        }

        [Test] public void SinglePackageResourceViewRestoresItsOwnerAndPerBundleSizeColumn()
        {
            Open(); panel.ImportReport(report); Finish(panel.Session); panel.InspectPackage("B");
            var open = panel.Q<ScrollView>("analysis-inspector").Query<Button>().ToList().Single(button => button.text == "查看此包的资源检查结果");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = open; open.SendEvent(submit); }
            Reopen();
            var results = panel.Q<AssetCheckResultsView>("resource-results");
            Assert.AreEqual(DisplayStyle.Flex, results.style.display.value);
            Assert.AreEqual(1, results.Model.Files.Count);
            Assert.AreEqual(texture, results.Model.Files.Single().Path);
            Assert.AreEqual("本包大小", results.Q<MultiColumnListView>().columns["size"].title);
            Assert.That(panel.Q<Label>("analysis-counts").text, Does.Contain("所属包：B"));
        }

        [Test] public void DifferentReportsAreIndependentAndCancelledNewReportDoesNotPublish()
        {
            Open(); panel.ImportReport(report); Finish(panel.Session); panel.Dispose(); panel = null;
            var originalRecord = cache.Records().Single(); var original = File.ReadAllBytes(originalRecord.DataPath);
            var next = Path.Combine(temp, "next.report"); Write(next, new[] { Bundle("New", 10, Array.Empty<string>()) });
            panel = new BundleAnalysisPanel(() => workspace, () => { }); panel.ImportReport(next); panel.Session.Cancel(); panel.Dispose(); panel = null;
            Assert.AreEqual(original, File.ReadAllBytes(originalRecord.DataPath)); Assert.AreEqual(1, cache.Records().Length);
            panel = new BundleAnalysisPanel(() => workspace, () => { }); panel.ImportReport(next); Finish(panel.Session); panel.Dispose(); panel = null;
            Assert.AreEqual(2, cache.Records().Length); Assert.AreEqual(original, File.ReadAllBytes(originalRecord.DataPath));
            using (var restored = cache.Load().Session)
            { Assert.AreEqual(Path.GetFullPath(Path.GetFileName(next)), restored.Packages.Report.SourcePath); Assert.AreEqual(0, restored.Resources.Length); }
        }

        [Test] public void SaveFailureIsVisibleAndDoesNotDiscardCompletedResults()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cache.DirectoryPath)); File.WriteAllText(cache.DirectoryPath, "blocked");
            Open(); panel.ImportReport(report);
            for (var i = 0; panel.Running && i < 10000; i++) panel.Pump();
            Assert.IsTrue(panel.Session.Complete);
            Assert.That(panel.Q<Label>("analysis-status").text, Does.Contain("历史保存失败"));
        }

        [Test] public void CacheIsProjectScopedAndContainsOriginalReportWithoutCopyingAssetBundles()
        {
            using (var session = Inspect()) cache.Save(session, new BundleAnalysisViewState());
            Assert.IsNull(new BundleAnalysisCache(Path.Combine(temp, "OtherProject")).Load().Session);
            var saved = JsonUtility.FromJson<BundleAnalysisCacheData>(File.ReadAllText(DataFile));
            CollectionAssert.AreEqual(File.ReadAllBytes(report), Convert.FromBase64String(saved.reportBytes));
            Assert.AreEqual(2, Directory.GetFiles(cache.Records().Single().DirectoryPath).Length);
            Assert.IsEmpty(Directory.GetFiles(cache.DirectoryPath, "*.bundle", SearchOption.AllDirectories));
        }
        [Test] public void SharedSnapshotOpensInAnotherProjectWithoutLibraryOrOriginalReport()
        {
            string evidence;
            using (var session = Inspect())
            {
                evidence = Evidence(session);
                cache.Save(session, new BundleAnalysisViewState { search = "personal", directory = root });
            }
            var original = cache.Records().Single();
            var otherRoot = Path.Combine(temp, "OtherProject");
            var other = new BundleAnalysisCache(otherRoot);
            var copied = Path.Combine(other.DirectoryPath, original.Id);
            Directory.CreateDirectory(copied);
            foreach (var source in Directory.GetFiles(original.DirectoryPath)) File.Copy(source, Path.Combine(copied, Path.GetFileName(source)));
            File.Delete(report);
            Assert.IsFalse(Directory.Exists(Path.Combine(otherRoot, "Library")));
            var loaded = other.Load();
            using (loaded.Session)
            {
                Assert.IsTrue(loaded.Session.IsSnapshot);
                Assert.AreEqual(evidence, Evidence(loaded.Session));
                Assert.AreEqual("", loaded.View.search);
                Assert.AreEqual("", loaded.View.directory);
            }
            Assert.IsFalse(Directory.Exists(Path.Combine(otherRoot, "Library")), "Reading alone creates no personal state.");
            var record = other.Records().Single();
            var before = File.ReadAllBytes(record.DataPath);
            other.SaveView(record, new BundleAnalysisViewState { search = "another user" });
            CollectionAssert.AreEqual(before, File.ReadAllBytes(record.DataPath));
            Assert.AreEqual(2, Directory.GetFiles(record.DirectoryPath).Length);
            Assert.That(record.ViewPath, Does.StartWith(Path.Combine(otherRoot, "Library")));
            Assert.AreEqual("fixture.report", JsonUtility.FromJson<BundleAnalysisCacheData>(File.ReadAllText(record.DataPath)).reportPath);
        }

        [Test] public void ClearingLibraryPreservesSharedHistoryAndUsesDefaultView()
        {
            using (var session = Inspect()) cache.Save(session, new BundleAnalysisViewState { search = "personal" });
            Directory.Delete(Path.Combine(temp, "Library"), true);
            var loaded = new BundleAnalysisCache(temp).Load();
            using (loaded.Session)
            {
                Assert.IsNotNull(loaded.Session);
                Assert.AreEqual("", loaded.View.search);
            }
            Assert.IsTrue(File.Exists(DataFile));
        }

        [Test] public void MissingPersonalLastSelectionAfterSharedHistoryChangesUsesAvailableRecord()
        {
            using (var session = Inspect()) cache.Save(session, new BundleAnalysisViewState());
            File.WriteAllText(Path.Combine(temp, "Library/July/AssetCheck/BundleReports/last.json"), "{\"id\":\"removed-in-another-branch\"}");
            using (var restored = cache.Load().Session) Assert.IsNotNull(restored);
            Assert.Throws<InvalidDataException>(() => cache.Load("explicit-missing-record"));
        }

        [Test] public void CopiedReportUsesOneRecordAndLatestCompleteInspectionReplacesOnlyItsEvidence()
        {
            using (var initial = Inspect()) cache.Save(initial, new BundleAnalysisViewState { search = "initial" });
            var record = cache.Records().Single(); var id = record.Id;
            var copy = Path.Combine(temp, "renamed.report"); File.Copy(report, copy);
            workspace.ChangeSettings(settings => settings.CurrentStandards.texture.allowSpriteMipMaps = true);
            using (var current = new BundleInspectionSession(BundleBuildReport.Read(copy), workspace.Settings, () => false))
            { Finish(current); cache.Save(current, new BundleAnalysisViewState { search = "latest" }); }
            Assert.AreEqual(1, cache.Records().Length); Assert.AreEqual(id, cache.Records().Single().Id);
            var loaded = cache.Load(id); using (loaded.Session)
            {
                Assert.AreEqual(Path.GetFullPath(Path.GetFileName(copy)), loaded.Session.Packages.Report.SourcePath);
                Assert.IsTrue(loaded.Session.Settings.CurrentStandards.texture.allowSpriteMipMaps);
                Assert.AreEqual("latest", loaded.View.search);
                Assert.IsFalse(loaded.Session.Resources.SelectMany(resource => resource.Issues).Any(issue => issue.RuleId == "texture.sprite-mipmaps"));
            }
        }

        [Test] public void SameFilenameWithDifferentContentsNeverOverwritesAnExistingRecord()
        {
            using (var initial = Inspect()) cache.Save(initial, new BundleAnalysisViewState());
            var firstRecord = cache.Records().Single(); var original = File.ReadAllBytes(firstRecord.DataPath);
            var other = Path.Combine(temp, "Other"); Directory.CreateDirectory(other); other = Path.Combine(other, Path.GetFileName(report));
            Write(other, new[] { Bundle("Other", 999, Array.Empty<string>()) });
            using (var current = new BundleInspectionSession(BundleBuildReport.Read(other), workspace.Settings, () => false))
            { Finish(current); cache.Save(current, new BundleAnalysisViewState()); }
            var records = cache.Records(); Assert.AreEqual(2, records.Length);
            Assert.AreEqual(2, records.Select(record => record.Id).Distinct().Count());
            Assert.IsTrue(records.All(record => record.reportName == "fixture.report" && record.Id.StartsWith("fixture_")));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(firstRecord.DataPath));
            using (var initial = cache.Load(firstRecord.Id).Session) Assert.AreEqual(300, initial.Packages.Report.TotalBytes);
        }

        [Test] public void ClosingAnOlderSnapshotOnlyUpdatesViewAndCannotOverwriteANewerInspection()
        {
            using (var firstInspection = Inspect()) cache.Save(firstInspection, new BundleAnalysisViewState());
            using (var old = cache.Load().Session)
            {
                workspace.ChangeSettings(settings => settings.CurrentStandards.texture.allowSpriteMipMaps = true);
                using (var latest = Inspect()) cache.Save(latest, new BundleAnalysisViewState());
                var before = File.ReadAllBytes(DataFile);
                cache.Save(old, new BundleAnalysisViewState { search = "old view" });
                CollectionAssert.AreEqual(before, File.ReadAllBytes(DataFile));
                var restored = cache.Load(); using (restored.Session)
                { Assert.IsTrue(restored.Session.Settings.CurrentStandards.texture.allowSpriteMipMaps); Assert.AreEqual("old view", restored.View.search); }
            }
        }

        [Test] public void ReportBomAndItsExactSizeBindingSurviveOriginalDirectoryCleanup()
        {
            File.WriteAllBytes(report, new byte[] { 0xef, 0xbb, 0xbf }.Concat(File.ReadAllBytes(report)).ToArray());
            WriteSizes(); var original = File.ReadAllBytes(report);
            using (var session = Inspect()) cache.Save(session, new BundleAnalysisViewState());
            File.Delete(report); File.Delete(report + ".sizes.json");
            using (var restored = cache.Load().Session)
            {
                CollectionAssert.AreEqual(original, restored.Packages.Report.ReportBytes);
                Assert.AreEqual(100, restored.Packages.Report.Sizes.Bundle("A").UnpackedBytes);
                Assert.IsFalse(restored.Running);
            }
        }

        [Test] public void FailedAtomicReplacementKeepsThePreviousCompleteInspection()
        {
            using (var initial = Inspect()) cache.Save(initial, new BundleAnalysisViewState());
            var original = File.ReadAllBytes(DataFile);
            workspace.ChangeSettings(settings => settings.CurrentStandards.texture.allowSpriteMipMaps = true);
            using (var latest = Inspect())
            using (var held = new FileStream(DataFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => cache.Save(latest, new BundleAnalysisViewState()));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(DataFile));
            using (var restored = cache.Load().Session) Assert.IsFalse(restored.Settings.CurrentStandards.texture.allowSpriteMipMaps);
        }

        [Test] public void BadHistoryMetadataDoesNotHideOtherReportResults()
        {
            using (var initial = Inspect()) cache.Save(initial, new BundleAnalysisViewState());
            var broken = cache.Records().Single();
            var other = Path.Combine(temp, "other.report"); Write(other, new[] { Bundle("Other", 10, Array.Empty<string>()) });
            using (var current = new BundleInspectionSession(BundleBuildReport.Read(other), workspace.Settings, () => false))
            { Finish(current); cache.Save(current, new BundleAnalysisViewState()); }
            File.WriteAllText(Path.Combine(broken.DirectoryPath, "record.json"), "{broken");
            var records = cache.Records(); Assert.AreEqual(1, records.Length); Assert.That(cache.Notice, Does.Contain("部分历史记录无法读取"));
            using (var restored = cache.Load(records.Single().Id).Session) Assert.AreEqual(10, restored.Packages.Report.TotalBytes);
        }

        [Test] public void ExplicitRecheckUsesArchivedReportWhenOriginalReportWasDeleted()
        {
            Open(); panel.ImportReport(report); Finish(panel.Session); Reopen(); File.Delete(report);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texture); importer.mipmapEnabled = false; importer.SaveAndReimport();
            var button = panel.Q<Button>("recheck-analysis-history"); Assert.IsTrue(button.enabledSelf);
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
            Finish(panel.Session); Assert.IsFalse(panel.Session.IsSnapshot);
            Assert.IsFalse(panel.Session.Resources.SelectMany(resource => resource.Issues).Any(issue => issue.RuleId == "texture.sprite-mipmaps"));
            Assert.AreEqual(1, cache.Records().Length);
        }

        [Test] public void InspectionLoadingDoesNotDependOnOptionalBrowsingState()
        {
            using (var firstInspection = Inspect()) cache.Save(firstInspection, new BundleAnalysisViewState());
            var record = cache.Records().Single(); File.WriteAllText(record.ViewPath, "{broken");
            using (var inspection = cache.LoadInspection(record.Id)) { Assert.IsTrue(inspection.Complete); Assert.IsFalse(inspection.Running); }
            Assert.Throws<InvalidDataException>(() => cache.Load(record.Id));
        }

        [Test] public void MetadataCannotMislabelTheArchivedPlatformOrPackage()
        {
            using (var firstInspection = Inspect()) cache.Save(firstInspection, new BundleAnalysisViewState());
            var record = cache.Records().Single(); record.package = "WrongPackage";
            File.WriteAllText(Path.Combine(record.DirectoryPath, "record.json"), JsonUtility.ToJson(record));
            Assert.Throws<InvalidDataException>(() => cache.LoadInspection(record.Id));
        }

        [Test] public void SelectingAnUnreadableHistoryShowsItsErrorAndKeepsTheDisplayedResult()
        {
            using (var initial = Inspect()) cache.Save(initial, new BundleAnalysisViewState());
            var good = cache.Records().Single();
            var other = Path.Combine(temp, "other.report"); Write(other, new[] { Bundle("Other", 10, Array.Empty<string>()) });
            using (var current = new BundleInspectionSession(BundleBuildReport.Read(other), workspace.Settings, () => false))
            { Finish(current); cache.Save(current, new BundleAnalysisViewState()); }
            Open();
            var picker = panel.Q<PopupField<BundleAnalysisRecord>>("analysis-history");
            var open = panel.Q<Button>("open-analysis-history");
            picker.value = picker.choices.Single(record => record.Id == good.Id);
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = open; open.SendEvent(submit); }
            var displayed = panel.Session;
            var bad = picker.choices.Single(record => record.Id != good.Id); File.WriteAllText(bad.DataPath, "{broken"); picker.value = bad;
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = open; open.SendEvent(submit); }
            Assert.AreSame(displayed, panel.Session);
            Assert.That(panel.Q<Label>("analysis-status").text, Does.Contain("历史分析未恢复"));
        }

    }
}
