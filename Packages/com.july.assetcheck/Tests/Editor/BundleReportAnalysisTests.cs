using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace July.AssetCheck.Tests
{
    public sealed class BundleReportAnalysisTests
    {
        private string _root, _temp;

        [SetUp]
        public void Setup()
        {
            _root = "Assets/__BundleAnalysis_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_root));
            foreach (var name in new[] { "A", "B", "Shared" }) AssetDatabase.CreateFolder(_root, name);
            _temp = Path.GetFullPath("Temp/BundleAnalysis_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
        }

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(_root);
            Assert.That(_temp, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar));
            Directory.Delete(_temp, true);
        }

        [Test]
        public void ReadsReportAndSortsRealBundleSizesWithoutChangingSource()
        {
            var path = Write(Fixture());
            var before = File.ReadAllBytes(path);
            var analysis = new BundleReportAnalysis(BundleBuildReport.Read(path));
            var projection = analysis.Project(Settings());
            Assert.That(analysis.Report.TotalBytes, Is.EqualTo(600));
            Assert.That(projection.Rows(BundleAnalysisMode.BundleSizes).Select(row => row.Name), Is.EqualTo(new[] { "D", "B", "A" }));
            Assert.That(projection.Rows(BundleAnalysisMode.Granularity).Select(row => row.Name), Is.EqualTo(new[] { "A", "B", "D" }));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void DependenciesDeduplicateDiamondAndHandleCycles()
        {
            var fixture = Fixture();
            fixture.BundleInfos[2].DependBundles = new[] { "A" };
            var analysis = Read(fixture);
            Assert.That(analysis.Closure(new[] { "A", "D" }), Is.EquivalentTo(new[] { "A", "B", "D" }));
            Assert.That(analysis.Bytes(analysis.Closure(new[] { "A", "D" })), Is.EqualTo(600));
            Assert.That(analysis.References("D"), Is.EquivalentTo(new[] { "A", "B" }));
            var projection = analysis.Project(Settings());
            Assert.That(projection.Rows(BundleAnalysisMode.Dependencies, BundleDependencyMetric.BundleClosure).First(row => row.Path.EndsWith("one.prefab")).Bytes, Is.EqualTo(600));
        }

        [Test]
        public void AssetLoadSetsKeepReportDependenciesSeparateFromBundleClosureAndUnknownContents()
        {
            var fixture = Fixture();
            fixture.AssetInfos = new[] { fixture.AssetInfos[0] };
            fixture.AssetInfos[0].DependBundles = new[] { "A", "B", "B" };
            fixture.Summary.MainAssetTotalCount = 1;
            fixture.BundleInfos[2].DependBundles = new[] { "A" };
            var projection = Read(fixture).Project(Settings());
            var loadRows = projection.Rows(BundleAnalysisMode.Dependencies);
            var known = loadRows.Single(row => row.Path.EndsWith("one.prefab"));
            Assert.That(known.Bundles, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(known.Bytes, Is.EqualTo(300));
            Assert.That(loadRows.First(), Is.SameAs(known), "已知体积按降序展示，未提供的体积不能显示为零。");
            var unknown = loadRows.Single(row => row.Asset.Owners.Count == 2);
            Assert.That(unknown.Bytes, Is.Null);
            Assert.That(unknown.Bundles, Is.Empty);
            Assert.That(unknown.Description, Does.Contain("未提供独立加载集合"));
            Assert.That(projection.Details(known, BundleAnalysisMode.Dependencies).Where(detail => detail.Bundle != null)
                .Select(detail => detail.Bundle), Is.EqualTo(new[] { "A", "B" }), "资源加载集合不得额外显示包级依赖边为加载包。");
            var closure = projection.Rows(BundleAnalysisMode.Dependencies, BundleDependencyMetric.BundleClosure)
                .Single(row => row.Asset.Path == known.Asset.Path);
            Assert.That(closure.Bundles, Is.EqualTo(new[] { "A", "B", "D" }));
            Assert.That(closure.Bytes, Is.EqualTo(600));
            Assert.That(projection.Rows(BundleAnalysisMode.Dependencies, BundleDependencyMetric.BundleClosure)
                .Single(row => row.Asset.Path == unknown.Asset.Path).Bytes, Is.EqualTo(600));
        }

        [Test]
        public void MainResourceWithNoResourceDependenciesStillHasExactlyItsMainBundleInLoadSet()
        {
            var analysis = Read(Fixture());
            var projection = analysis.Project(Settings());
            var row = projection.Rows(BundleAnalysisMode.Dependencies).Single(item => item.Path.EndsWith("one.prefab"));
            Assert.That(row.Bundles, Is.EqualTo(new[] { "A" }));
            Assert.That(row.Bytes, Is.EqualTo(100));
            var settings = Settings();
            settings.ignoredAssets.Add(new IgnoredAsset { guid = row.Asset.Guid, lastKnownPath = row.Asset.Path });
            projection = analysis.Project(settings);
            foreach (BundleDependencyMetric metric in Enum.GetValues(typeof(BundleDependencyMetric)))
                Assert.That(projection.Rows(BundleAnalysisMode.Dependencies, metric).Any(item => item.Path == row.Path), Is.False);
        }

        [Test]
        public void DuplicatePathsUseDistinctOwnersAndNeverClaimDuplicateBytes()
        {
            var fixture = Fixture();
            var duplicate = fixture.BundleInfos[0].BundleContents[1];
            fixture.BundleInfos[0].BundleContents = fixture.BundleInfos[0].BundleContents.Concat(new[] { duplicate }).ToArray();
            var projection = Read(fixture).Project(Settings());
            var rows = projection.Rows(BundleAnalysisMode.Duplicates);
            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(rows[0].Asset.Owners, Is.EquivalentTo(new[] { "A", "B" }));
            Assert.That(rows[0].Bytes, Is.Null);
            Assert.That(rows[0].Bundles, Is.EquivalentTo(new[] { "A", "B" }));
            Assert.That(projection.Details(rows[0], BundleAnalysisMode.Duplicates).Count(detail => detail.Bundle != null), Is.EqualTo(2));
        }

        [Test]
        public void ResourceIndexIncludesNonAddressableBundleContents()
        {
            var fixture = Fixture();
            fixture.AssetInfos = new[] { fixture.AssetInfos[0] };
            fixture.Summary.MainAssetTotalCount = 1;
            var analysis = Read(fixture);
            Assert.That(analysis.Assets.Count, Is.EqualTo(4));
            Assert.That(analysis.Project(Settings()).Rows(BundleAnalysisMode.Assets).Count, Is.EqualTo(4));
        }

        [Test]
        public void ScopeAndIgnoresFilterAssetsButKeepActualDependencyCosts()
        {
            var analysis = Read(Fixture());
            var settings = Settings();
            settings.scope = _root + "/A";
            var projection = analysis.Project(settings);
            Assert.That(projection.Assets.Count, Is.EqualTo(1));
            Assert.That(projection.RelatedBundles, Is.EquivalentTo(new[] { "A", "B", "D" }));
            Assert.That(projection.RelatedBytes, Is.EqualTo(600));
            var details = projection.Details(projection.BundleRow("B"), BundleAnalysisMode.BundleSizes);
            Assert.That(details.Where(detail => detail.Asset != null), Is.Empty, "依赖包上下文不能泄露范围外资源条目。");
            settings.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(_root + "/A"), lastKnownPath = _root + "/A" });
            projection = analysis.Project(settings);
            Assert.That(projection.Assets, Is.Empty);
            Assert.That(projection.IgnoredCount, Is.EqualTo(1));
            Assert.That(analysis.Report.TotalBytes, Is.EqualTo(600), "忽略不能从实际整包体积中扣除。");
        }

        [Test]
        public void SingleFileIgnoreAlsoHidesDuplicateAndDetailEntries()
        {
            var analysis = Read(Fixture());
            var settings = Settings();
            var duplicate = analysis.Assets.Single(asset => asset.Owners.Count == 2);
            settings.ignoredAssets.Add(new IgnoredAsset { guid = duplicate.Guid, lastKnownPath = duplicate.Path });
            var projection = analysis.Project(settings);
            Assert.That(projection.Rows(BundleAnalysisMode.Duplicates), Is.Empty);
            Assert.That(projection.IgnoredCount, Is.EqualTo(1));
            Assert.That(projection.Details(projection.BundleRow("A"), BundleAnalysisMode.BundleSizes)
                .Where(detail => detail.Asset != null).Any(detail => detail.Asset.Path == duplicate.Path), Is.False);
        }

        [Test]
        public void DirectoryTotalsAreUnionCostsAndMayOverlap()
        {
            var projection = Read(Fixture()).Project(Settings());
            var rows = projection.Rows(BundleAnalysisMode.Directories);
            Assert.That(rows.Single(row => row.Path == _root + "/A").Bytes, Is.EqualTo(600));
            Assert.That(rows.Single(row => row.Path == _root + "/B").Bytes, Is.EqualTo(500));
            Assert.That(rows.Sum(row => row.Bytes.Value), Is.GreaterThan(600));
        }

        [Test]
        public void GuidMatchingFollowsMovesAndDoesNotRetargetReplacementAtOldPath()
        {
            var path = _root + "/original.asset";
            AssetDatabase.CreateAsset(new Texture2D(4, 4), path);
            var source = Asset(path);
            source.AssetGUID = AssetDatabase.AssetPathToGUID(path);
            var data = Report(Bundle("only", 50, new[] { source }));
            var analysis = Read(data);
            var record = analysis.Assets.Single();
            var moved = _root + "/B/moved.asset";
            Assert.That(AssetDatabase.MoveAsset(path, moved), Is.Empty);
            Assert.That(record.ResolveCurrentPath(), Is.EqualTo(moved));
            var settings = Settings(); settings.scope = _root + "/B";
            Assert.That(analysis.Project(settings).Assets.Count, Is.EqualTo(1));
            AssetDatabase.DeleteAsset(moved);
            AssetDatabase.CreateAsset(new Texture2D(4, 4), path);
            Assert.That(record.ResolveCurrentPath(), Is.Empty);
            Assert.That(analysis.Project(Settings()).UnmatchedCount, Is.EqualTo(1));
        }

        [TestCase("")]
        [TestCase("00000000000000000000000000000000")]
        public void MissingGuidUsesExplicitPathMatchingAndRespectsSingleFileIgnores(string guid)
        {
            var path = _root + "/path-only.asset";
            AssetDatabase.CreateAsset(new Texture2D(4, 4), path);
            var source = Asset(path); source.AssetGUID = guid;
            var analysis = Read(Report(Bundle("only", 1, new[] { source })));
            var projection = analysis.Project(Settings());
            Assert.That(projection.UnmatchedCount, Is.Zero);
            Assert.That(projection.MatchLabel(projection.Assets.Single()), Is.EqualTo("按路径匹配"));
            var settings = Settings();
            settings.ignoredAssets.Add(new IgnoredAsset { guid = AssetDatabase.AssetPathToGUID(path), lastKnownPath = path });
            Assert.That(analysis.Project(settings).Assets, Is.Empty);
        }

        [TestCase("manifest")]
        [TestCase("version")]
        [TestCase("negative")]
        [TestCase("duplicate")]
        [TestCase("dependency")]
        [TestCase("summary")]
        [TestCase("guid")]
        public void RejectsMalformedOrInconsistentReports(string failure)
        {
            var fixture = Fixture();
            var path = Path.Combine(_temp, "bad.report");
            if (failure == "manifest") { File.WriteAllText(path, "{\"PackageName\":\"DefaultPackage\"}"); Assert.Throws<InvalidDataException>(() => BundleBuildReport.Read(path)); return; }
            if (failure == "version") fixture.Summary.YooVersion = "9.0.0";
            if (failure == "negative") fixture.BundleInfos[0].FileSize = -1;
            if (failure == "duplicate") fixture.BundleInfos[1].BundleName = "A";
            if (failure == "dependency") fixture.BundleInfos[0].DependBundles = new[] { "missing" };
            if (failure == "summary") fixture.Summary.AllBundleTotalSize++;
            if (failure == "guid") fixture.BundleInfos[0].BundleContents[0].AssetGUID = "not-a-guid";
            path = Write(fixture);
            if (failure == "version") Assert.Throws<NotSupportedException>(() => BundleBuildReport.Read(path));
            else Assert.Throws<InvalidDataException>(() => BundleBuildReport.Read(path));
        }

        [Test]
        public void EmptyReportIsValidAndEveryViewIsEmpty()
        {
            var analysis = Read(Report());
            foreach (BundleAnalysisMode mode in Enum.GetValues(typeof(BundleAnalysisMode)))
                Assert.That(analysis.Project(Settings()).Rows(mode), Is.Empty);
        }

        private AssetCheckSettings Settings()
        {
            var settings = AssetCheckSettings.CreateDefault(); settings.scope = _root; return settings;
        }

        private BundleReportData Fixture()
        {
            var duplicate = Asset(_root + "/Shared/duplicate.mat");
            return Report(
                Bundle("A", 100, new[] { Asset(_root + "/A/one.prefab"), duplicate }, "B", "D"),
                Bundle("B", 200, new[] { Asset(_root + "/B/two.prefab"), duplicate }, "D"),
                Bundle("D", 300, new[] { Asset(_root + "/Shared/dependency.png") }));
        }

        private static BundleReportAssetData Asset(string path) => new BundleReportAssetData
            { AssetPath = path, AssetGUID = Guid.NewGuid().ToString("N"), DependBundles = Array.Empty<string>() };

        private static BundleReportBundleData Bundle(string name, long bytes, BundleReportAssetData[] assets, params string[] dependencies) =>
            new BundleReportBundleData { BundleName = name, FileName = name + ".bundle", FileSize = bytes, BundleContents = assets, DependBundles = dependencies, Tags = Array.Empty<string>() };

        private static BundleReportData Report(params BundleReportBundleData[] bundles)
        {
            var assets = bundles.SelectMany(bundle => bundle.BundleContents.Select(source => new BundleReportAssetData
            {
                AssetPath = source.AssetPath, AssetGUID = source.AssetGUID, MainBundleName = bundle.BundleName, DependBundles = Array.Empty<string>()
            })).GroupBy(asset => asset.AssetPath).Select(group => group.First()).ToArray();
            return new BundleReportData
            {
                Summary = new BundleReportSummary
                {
                    YooVersion = "2.3.12", UnityVersion = "2022.3", BuildDate = "2026/10/30 12:00:00", BuildPackageName = "Test", BuildPackageVersion = "1",
                    BuildTarget = (int)BuildTarget.WebGL, AllBundleTotalCount = bundles.Length, AllBundleTotalSize = bundles.Sum(bundle => bundle.FileSize), MainAssetTotalCount = assets.Length
                },
                BundleInfos = bundles, AssetInfos = assets
            };
        }

        private BundleReportAnalysis Read(BundleReportData data) => new BundleReportAnalysis(BundleBuildReport.Read(Write(data)));
        private string Write(BundleReportData data)
        {
            var path = Path.Combine(_temp, "fixture.report"); File.WriteAllText(path, JsonUtility.ToJson(data)); return path;
        }
    }
}
