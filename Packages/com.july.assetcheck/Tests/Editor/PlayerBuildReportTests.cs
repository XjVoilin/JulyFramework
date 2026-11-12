using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.AssetCheck.Tests
{
    public sealed class PlayerBuildReportTests
    {
        private string _root;
        private PlayerBuildReportStore _store;
        private EditorWindow _window;
        private string Target => EditorUserBuildSettings.activeBuildTarget.ToString();

        [SetUp]
        public void Setup()
        {
            _root = Path.GetFullPath("Temp/PlayerBuildReport_" + Guid.NewGuid().ToString("N"));
            _store = new PlayerBuildReportStore(_root);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_window != null) _window.Close();
            Assert.That(_root, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar));
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private PlayerBuildSnapshot Snapshot(bool clean, int count = 2, string time = "2026-11-12T03:00:00Z")
        {
            var assets = new PlayerBuildAsset[count];
            for (var i = 0; i < count; i++)
                assets[i] = new PlayerBuildAsset { path = "Assets/item" + i + ".asset", guid = "", type = "Texture2D", bytes = 100 };
            return new PlayerBuildSnapshot { platform = Target, builtAt = time, outputPath = "client/" + time,
                unityVersion = "2022.3", cleanBuild = clean, assets = assets, assetBytes = (ulong)count * 100, totalBytes = 1000 };
        }

        [Test]
        public void EmptyIncrementalPreservesWholeDetailedSnapshotAndItsOrigin()
        {
            var first = Snapshot(true); _store.Record(first, BuildResult.Succeeded);
            _store.Record(Snapshot(false, 0, "2026-11-12T04:00:00Z"), BuildResult.Succeeded);
            var saved = _store.Load(Target);
            Assert.That(saved.LatestDetails.assets.Length, Is.EqualTo(2));
            Assert.That(saved.LatestDetails.builtAt, Is.EqualTo(first.builtAt));
            Assert.That(saved.LatestDetails.outputPath, Is.EqualTo(first.outputPath));
            Assert.That(saved.lastHadDetails, Is.False);
            Assert.That(saved.lastBuiltAt, Is.EqualTo("2026-11-12T04:00:00Z"));
        }

        [Test]
        public void PartialReportDoesNotOverwriteOrMergeTheCleanBaseline()
        {
            _store.Record(Snapshot(true), BuildResult.Succeeded);
            _store.Record(Snapshot(false, 1, "2026-11-12T04:00:00Z"), BuildResult.Succeeded);
            var cache = _store.Load(Target);
            Assert.That(cache.baseline.assets.Length, Is.EqualTo(2));
            Assert.That(cache.incremental.assets.Length, Is.EqualTo(1));
            Assert.That(cache.LatestDetails.cleanBuild, Is.False);
            _store.Record(Snapshot(false, 0), BuildResult.Succeeded);
            Assert.That(_store.Load(Target).LatestDetails.assets.Length, Is.EqualTo(1));
            Assert.That(_store.Load(Target).baseline.assets.Length, Is.EqualTo(2));
        }

        [Test]
        public void NewCleanReportReplacesBaselineAndClearsOldIncremental()
        {
            _store.Record(Snapshot(true), BuildResult.Succeeded);
            _store.Record(Snapshot(false, 1), BuildResult.Succeeded);
            _store.Record(Snapshot(true, 3), BuildResult.Succeeded);
            Assert.That(_store.Load(Target).incremental, Is.Null);
            Assert.That(_store.Load(Target).LatestDetails.assets.Length, Is.EqualTo(3));
        }

        [Test]
        public void FailedAndTemporaryScriptsOnlyBuildsDoNotChangeCache()
        {
            _store.Record(Snapshot(true), BuildResult.Succeeded);
            var path = Path.Combine(_store.DirectoryPath, Target + ".json");
            var original = File.ReadAllBytes(path);
            Assert.That(_store.Record(Snapshot(false), BuildResult.Failed), Is.False);
            var aot = Snapshot(true, 3); aot.scriptsOnly = true;
            Assert.That(_store.Record(aot, BuildResult.Succeeded), Is.False);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }

        [Test]
        public void PlatformCachesAreIndependentAndViewingDoesNotRewrite()
        {
            _store.Record(Snapshot(true), BuildResult.Succeeded);
            var other = Snapshot(false, 1); other.platform = Target == "WebGL" ? "StandaloneWindows64" : "WebGL";
            _store.Record(other, BuildResult.Succeeded);
            var path = Path.Combine(_store.DirectoryPath, Target + ".json");
            var original = File.ReadAllBytes(path);
            Assert.That(new PlayerBuildReportStore(_root).Load(Target).LatestDetails.assets.Length, Is.EqualTo(2));
            Assert.That(_store.Load(other.platform).LatestDetails.assets.Length, Is.EqualTo(1));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }

        [Test]
        public void LegacySnapshotKeepsUnknownCoverageAndSurvivesEmptyBuild()
        {
            var legacy = Snapshot(false); legacy.version = 1;
            var path = Path.Combine(_root, PlayerBuildReportStore.LegacyPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(legacy));
            var original = File.ReadAllBytes(path);
            Assert.That(_store.Load(Target).LatestDetails.version, Is.EqualTo(1));
            Assert.That(Directory.Exists(_store.DirectoryPath), Is.False);
            _store.Record(Snapshot(false, 0), BuildResult.Succeeded);
            Assert.That(_store.Load(Target).baseline.assets.Length, Is.EqualTo(2));
            Assert.That(_store.Load(Target).baseline.cleanBuild, Is.False);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }

        [Test]
        public void EmptyFirstBuildHasNoInventedResourceSnapshot()
        {
            _store.Record(Snapshot(false, 0), BuildResult.Succeeded);
            Assert.That(_store.Load(Target).LatestDetails, Is.Null);
            Assert.That(_store.Load(Target).lastHadDetails, Is.False);
        }

        [Test]
        public void MalformedCacheIsReportedAndNotReplacedWithSuccess()
        {
            _store.Record(Snapshot(true), BuildResult.Succeeded);
            var path = Path.Combine(_store.DirectoryPath, Target + ".json");
            File.WriteAllText(path, "{\"version\":999}");
            Assert.Throws<InvalidDataException>(() => _store.Load(Target));
            Assert.That(File.ReadAllText(path), Does.Contain("999"));
            File.WriteAllText(path, "not json");
            Assert.Throws<InvalidDataException>(() => _store.Load(Target));
            Assert.Throws<InvalidDataException>(() => _store.Load("../outside"));
        }

        private PlayerBuildAnalysisPanel Panel()
        {
            _window = ScriptableObject.CreateInstance<EditorWindow>();
            var panel = new PlayerBuildAnalysisPanel(_store);
            _window.rootVisualElement.Add(panel);
            _window.Show(); panel.Activate(); return panel;
        }

        [Test]
        public void PageShowsCachedOriginsAndExplainsNewEmptyReport()
        {
            var old = Snapshot(true); _store.Record(old, BuildResult.Succeeded);
            var newer = Snapshot(false, 0, "2026-11-12T04:00:00Z"); newer.totalBytes = 999999;
            _store.Record(newer, BuildResult.Succeeded);
            var panel = Panel();
            Assert.That(panel.Q<Label>("player-build-notice").text, Does.Contain("未提供资源明细"));
            Assert.That(panel.Q<Label>("player-build-output").text, Does.Contain(old.outputPath));
            Assert.That(panel.Q<MultiColumnListView>("player-build-assets").itemsSource.Count, Is.EqualTo(2));
            Assert.That(panel.Q<Label>("player-build-metadata").text, Does.Contain("完整构建提供"));
        }

        [Test]
        public void PageCanViewIncrementalAndRetainedCleanReportWithoutMixingRows()
        {
            _store.Record(Snapshot(true), BuildResult.Succeeded);
            _store.Record(Snapshot(false, 1), BuildResult.Succeeded);
            var panel = Panel();
            Assert.That(panel.Q<Label>("player-build-metadata").text, Does.Contain("增量报告"));
            Assert.That(panel.Q<MultiColumnListView>("player-build-assets").itemsSource.Count, Is.EqualTo(1));
            var button = panel.Q<Button>("player-build-baseline");
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
            Assert.That(panel.Q<Label>("player-build-metadata").text, Does.Contain("完整构建提供"));
            Assert.That(panel.Q<MultiColumnListView>("player-build-assets").itemsSource.Count, Is.EqualTo(2));
        }
    }
}
