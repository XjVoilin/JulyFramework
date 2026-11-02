using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.AssetCheck.Tests
{
    public sealed class BundleReportScopeTests
    {
        private string _root, _temp, _path, _ignoredFolder;
        private AssetCheckWorkspace _workspace;
        private BundleAnalysisPanel _panel;
        private BundleReportData _data;

        [SetUp]
        public void Setup()
        {
            _root = "Assets/__ReportReview_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            AssetDatabase.CreateFolder(_root, "Ignored");
            AssetDatabase.CreateFolder(_root, "IgnoredSibling");
            _ignoredFolder = _root + "/Ignored";
            _temp = Path.GetFullPath("Temp/ReportReview_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
            var x = Asset(_root + "/main.asset");
            var y = Asset(_ignoredFolder + "/ignored.asset");
            var z = Asset(_root + "/IgnoredSibling/visible.asset");
            _data = new BundleReportData
            {
                Summary = new BundleReportSummary
                {
                    YooVersion = "2.3.12", UnityVersion = "2022.3", BuildDate = "2026/10/30", BuildPackageName = "Review", BuildPackageVersion = "1",
                    BuildTarget = (int)BuildTarget.WebGL, AllBundleTotalCount = 2, AllBundleTotalSize = 300, MainAssetTotalCount = 1
                },
                BundleInfos = new[]
                {
                    new BundleReportBundleData { BundleName = "A", FileName = "a.bundle", FileSize = 100, DependBundles = new[] { "B" }, BundleContents = new[] { x, y } },
                    new BundleReportBundleData { BundleName = "B", FileName = "b.bundle", FileSize = 200, DependBundles = Array.Empty<string>(), BundleContents = new[] { y, z } }
                },
                AssetInfos = new[] { new BundleReportAssetData { AssetPath = x.AssetPath, AssetGUID = x.AssetGUID, MainBundleName = "A", DependBundles = new[] { "B" } } }
            };
            _path = Path.Combine(_temp, "input.report");
            File.WriteAllText(_path, JsonUtility.ToJson(_data));
            _workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_temp));
            _workspace.ChangeSettings(settings => settings.scope = _root);
            _panel = new BundleAnalysisPanel(() => _workspace, () => { });
            _panel.ImportReport(_path);
            BundleAnalysisPanelTests.Finish(_panel.Session);
        }

        private static BundleReportAssetData Asset(string path)
        {
            AssetDatabase.CreateAsset(new Texture2D(4, 4), path);
            return new BundleReportAssetData { AssetPath = path, AssetGUID = AssetDatabase.AssetPathToGUID(path) };
        }

        [TearDown]
        public void Cleanup()
        {
            _panel.Dispose(); _workspace.Dispose();
            AssetDatabase.DeleteAsset(_root);
            Assert.That(_temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar));
            Directory.Delete(_temp, true);
        }

        [Test]
        public void ReportSnapshotIncludesIgnoredResourcesAfterFolderMove()
        {
            var snapshot = _panel.Session;
            _workspace.SetFoldersIgnored(new[] { _ignoredFolder }, true);
            Assert.That(AssetDatabase.MoveAsset(_ignoredFolder, _root + "/Moved"), Is.Empty);
            _panel.SynchronizeSettings();
            Assert.That(_panel.Session, Is.SameAs(snapshot));
            Assert.That(snapshot.Resources.Length, Is.EqualTo(3));
            Assert.That(snapshot.Packages.Report.TotalBytes, Is.EqualTo(300));
            var asset = snapshot.Resources.Single(r => r.Asset.Path.EndsWith("ignored.asset")).Asset;
            Assert.That(asset.ResolveCurrentPath(), Does.Contain("/Moved/"));
            Assert.That(new AssetCheckExclusions(_workspace.Settings).Contains(asset.Guid, asset.ResolveCurrentPath()), Is.True);
        }

        [TestCase("ignore")]
        [TestCase("scope")]
        [TestCase("invalid")]
        [TestCase("removed")]
        public void ExternalConfigurationDoesNotRewriteCompletedReportAndReloadDoesNotNeedSourceFile(string change)
        {
            var snapshot = _panel.Session;
            var updated = _workspace.Settings.Copy();
            updated.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(_ignoredFolder), lastKnownPath = _ignoredFolder });
            if (change == "scope") updated.scope = _root + "/IgnoredSibling";
            var path = Path.Combine(_temp, AssetCheckSettingsFile.FileName); var valid = JsonUtility.ToJson(updated);
            if (change == "removed") File.Delete(path); else File.WriteAllText(path, change == "invalid" ? "{broken" : valid);
            _workspace.PollExternalChanges(); _panel.SynchronizeSettings();
            Assert.That(_panel.Session, Is.SameAs(snapshot)); Assert.That(snapshot.Resources.Length, Is.EqualTo(3));
            Assert.That(_panel.Q<Button>("analyze-report").enabledSelf, Is.False);
            File.WriteAllText(path, valid); File.Delete(_path);
            _workspace.Dispose(); _workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_temp)); _panel.SynchronizeSettings();
            Assert.That(_panel.Session, Is.SameAs(snapshot)); Assert.That(_panel.Q<Button>("analyze-report").enabledSelf, Is.True);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void HistoricalIgnoreMetadataSurvivesMoveDeletionAndRestoreWithoutFilteringBuildReport(bool deleteFolder, bool reportAfterMove)
        {
            var asset = _data.BundleInfos[0].BundleContents[1]; var folderGuid = AssetDatabase.AssetPathToGUID(_ignoredFolder);
            _workspace.SetFoldersIgnored(new[] { _ignoredFolder }, true); var folder = _ignoredFolder;
            if (reportAfterMove)
            {
                folder = _root + "/Intermediate"; Assert.That(AssetDatabase.MoveAsset(_ignoredFolder, folder), Is.Empty);
                _workspace.ReadCurrentSettings(); asset.AssetPath = folder + "/ignored.asset";
                File.WriteAllText(_path, JsonUtility.ToJson(_data));
            }
            AssetDatabase.DeleteAsset(folder + "/ignored.asset");
            if (deleteFolder) AssetDatabase.DeleteAsset(folder); else AssetDatabase.MoveAsset(folder, _root + "/Moved");
            _workspace.Dispose(); _workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_temp));
            Assert.That(new AssetCheckExclusions(_workspace.Settings).ContainsHistorical(asset.AssetGUID, asset.AssetPath), Is.True);
            _panel.ImportReport(_path); BundleAnalysisPanelTests.Finish(_panel.Session);
            Assert.That(_panel.Session.Resources.Length, Is.EqualTo(3));
            Assert.That(_panel.Session.Resources.Single(r => r.Asset.Path.EndsWith("ignored.asset")).Unverified, Is.True);
            _workspace.Restore(folderGuid);
            Assert.That(new AssetCheckExclusions(_workspace.Settings).ContainsHistorical(asset.AssetGUID, asset.AssetPath), Is.False);
        }

        [Test]
        public void HistoricalAliasesDoNotIgnoreNewResourceInReplacementFolderAtOldPath()
        {
            _workspace.SetFoldersIgnored(new[] { _ignoredFolder }, true);
            var originalGuid = AssetDatabase.AssetPathToGUID(_ignoredFolder);
            Assert.That(AssetDatabase.DeleteAsset(_ignoredFolder), Is.True);
            // 先在不同路径创建，确保 Unity 不沿用已删除目录的 GUID。
            AssetDatabase.CreateFolder(_root, "Replacement");
            Assert.That(AssetDatabase.AssetPathToGUID(_root + "/Replacement"), Is.Not.EqualTo(originalGuid));
            Assert.That(AssetDatabase.MoveAsset(_root + "/Replacement", _ignoredFolder), Is.Empty);
            var path = _ignoredFolder + "/ignored.asset";
            var replacement = Asset(path);
            _data.BundleInfos[0].BundleContents[1].AssetGUID = replacement.AssetGUID;
            File.WriteAllText(_path, JsonUtility.ToJson(_data));
            _panel.ImportReport(_path);
            BundleAnalysisPanelTests.Finish(_panel.Session);
            Assert.That(_panel.Session.Resources.Single(row => row.Asset.Path == path).Asset.Guid, Is.EqualTo(replacement.AssetGUID));
            Assert.That(new AssetCheckExclusions(_workspace.Settings).Contains(replacement.AssetGUID, path), Is.False,
                "历史目录路径只可用于未匹配条目，不能进入资源检查／复制的现存资源判断。");
        }

        [Test]
        public void LegacyLiveFolderIsEnrichedButUnknownDeletedTargetIsExplicitlyReported()
        {
            var legacy = _workspace.Settings.Copy();
            var guid = AssetDatabase.AssetPathToGUID(_ignoredFolder);
            legacy.ignoredAssets.Add(new IgnoredAsset { guid = guid, lastKnownPath = _ignoredFolder });
            var config = Path.Combine(_temp, AssetCheckSettingsFile.FileName);
            File.WriteAllText(config, JsonUtility.ToJson(legacy));
            var loaded = new AssetCheckSettingsFile(_temp).LoadOrCreate();
            Assert.That(loaded.ignoredAssets.Single().targetType, Is.EqualTo(IgnoredTargetType.Folder));
            Assert.That(loaded.ignoredAssets.Single().folderPaths, Is.EqualTo(new[] { _ignoredFolder }));
            AssetDatabase.DeleteAsset(_ignoredFolder);
            // 模拟旧配置在目标删除前未被新版工具加载，身份无法从旧 GUID 恢复。
            File.WriteAllText(config, JsonUtility.ToJson(legacy));
            _workspace.Dispose();
            _workspace = new AssetCheckWorkspace(new AssetCheckSettingsFile(_temp));
            _panel.ImportReport(_path);
            BundleAnalysisPanelTests.Finish(_panel.Session);
            Assert.That(_panel.Session.Resources.Length, Is.EqualTo(3));
            Assert.That(new AssetCheckExclusions(_workspace.Settings).UnresolvedHistoricalRecords, Is.EqualTo(1));
            Assert.That(_workspace.Settings.ignoredAssets.Single().targetType, Is.EqualTo(IgnoredTargetType.Unknown));
        }

        [Test]
        public void LegacyMovedFolderRetainsKnownOldAndCurrentPathsAfterReload()
        {
            var legacy = _workspace.Settings.Copy();
            legacy.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(_ignoredFolder), lastKnownPath = _ignoredFolder });
            var moved = _root + "/Moved";
            Assert.That(AssetDatabase.MoveAsset(_ignoredFolder, moved), Is.Empty);
            var config = Path.Combine(_temp, AssetCheckSettingsFile.FileName);
            File.WriteAllText(config, JsonUtility.ToJson(legacy));
            var migrated = new AssetCheckSettingsFile(_temp).LoadOrCreate();
            Assert.That(migrated.version, Is.EqualTo(5));
            Assert.That(migrated.ignoredAssets.Single().folderPaths, Is.EqualTo(new[] { _ignoredFolder, moved }));
            AssetDatabase.DeleteAsset(moved);
            var reloaded = new AssetCheckSettingsFile(_temp).LoadOrCreate();
            var analysis = new BundleReportAnalysis(BundleBuildReport.Read(_path));
            Assert.That(analysis.Project(reloaded).Assets.Any(asset => asset.Path.EndsWith("/ignored.asset")), Is.False);
            Assert.That(analysis.Project(reloaded).UnresolvedIgnoreCount, Is.Zero);
        }

        [TestCase("type")]
        [TestCase("empty")]
        [TestCase("traversal")]
        [TestCase("duplicate")]
        public void RejectsInvalidHistoricalFolderMetadataAtConfigurationBoundary(string invalid)
        {
            var settings = _workspace.Settings.Copy();
            var record = new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(_ignoredFolder), lastKnownPath = _ignoredFolder,
                targetType = IgnoredTargetType.Folder, folderPaths = new[] { _ignoredFolder } };
            if (invalid == "type") record.targetType = IgnoredTargetType.File;
            if (invalid == "empty") record.folderPaths = Array.Empty<string>();
            if (invalid == "traversal") record.folderPaths = new[] { "Assets/../Other" };
            if (invalid == "duplicate") record.folderPaths = new[] { _ignoredFolder, _ignoredFolder };
            settings.ignoredAssets.Add(record);
            var config = Path.Combine(_temp, AssetCheckSettingsFile.FileName);
            File.WriteAllText(config, JsonUtility.ToJson(settings));
            var before = File.ReadAllBytes(config);
            Assert.Throws<InvalidDataException>(() => new AssetCheckSettingsFile(_temp).LoadOrCreate());
            Assert.That(File.ReadAllBytes(config), Is.EqualTo(before));
        }

        [Test]
        public void SaveRejectsContradictionBetweenRecordedTypeAndExistingGuidWithoutChangingFile()
        {
            var store = new AssetCheckSettingsFile(_temp);
            var settings = store.LoadOrCreate();
            settings.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(_ignoredFolder),
                targetType = IgnoredTargetType.File, lastKnownPath = _ignoredFolder });
            var path = Path.Combine(_temp, AssetCheckSettingsFile.FileName);
            var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => store.Save(settings));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }
    }
}
