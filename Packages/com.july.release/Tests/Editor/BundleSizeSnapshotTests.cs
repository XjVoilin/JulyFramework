using System;
using System.IO;
using System.Linq;
using July.Release.Editor;
using NUnit.Framework;
using UnityEngine;

namespace July.Release.Tests
{
    public sealed class BundleSizeSnapshotTests
    {
        const string GuidA = "a1234567890123456789012345678901";
        string root, report;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "JulySize_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); report = Path.Combine(root, "fixture.report"); File.WriteAllText(report, "{\"fixture\":true}"); }
        [TearDown] public void Cleanup() => Directory.Delete(root, true);
        static BundleSizeRecord Bundle(string name = "A", long bytes = 20) => new BundleSizeRecord { name = name, unpackedBytes = bytes + 80, unassignedBytes = 75,
            resources = new[] { new ResourceSizeRecord { guid = GuidA, path = "Assets/a.asset", bytes = bytes, coverage = AssetBundleSizeCoverage.Complete } },
            generated = new[] { new GeneratedSizeRecord { description = "包索引", bytes = 5 } } };
        void Write(params BundleSizeRecord[] bundles) => AssetBundleSizeSnapshot.Write(report, bundles);
        [Test] public void OldReportWithoutSidecarHasNoInventedSizes() => Assert.IsNull(AssetBundleSizeSnapshot.ReadIfPresent(report));
        [Test] public void RoundTripPreservesExactBytesGuidIdentityAndGeneratedData()
        {
            Write(Bundle()); var snapshot = AssetBundleSizeSnapshot.ReadIfPresent(report);
            var size = snapshot.Resource("A", GuidA.ToUpperInvariant(), "Assets/moved.asset"); Assert.AreEqual(20, size.Bytes); Assert.AreEqual(AssetBundleSizeCoverage.Complete, size.Coverage);
            Assert.AreEqual(100, snapshot.Bundle("A").UnpackedBytes); Assert.AreEqual(75, snapshot.Bundle("A").UnassignedBytes); Assert.AreEqual(5, snapshot.Bundle("A").Generated.Single().Bytes);
        }
        [Test] public void ArchivedJsonCanBeValidatedWithoutAnyOriginalFiles()
        {
            Write(Bundle()); var json = File.ReadAllText(AssetBundleSizeSnapshot.FilePath(report)); var hash = AssetBundleSizeSnapshot.Hash(report);
            File.Delete(AssetBundleSizeSnapshot.FilePath(report)); File.Delete(report);
            var snapshot = AssetBundleSizeSnapshot.ReadJson(json, hash);
            Assert.AreEqual(20, snapshot.Resource("A", GuidA, "Assets/a.asset").Bytes);
            Assert.Throws<InvalidDataException>(() => AssetBundleSizeSnapshot.ReadJson(json, new string('0', 64)));
        }
        [TestCase(null)] [TestCase("")] [TestCase("invalid")]
        public void ArchivedJsonRequiresARealReportFingerprint(string hash)
        {
            Write(Bundle()); var json = File.ReadAllText(AssetBundleSizeSnapshot.FilePath(report));
            Assert.Throws<ArgumentException>(() => AssetBundleSizeSnapshot.ReadJson(json, hash));
        }
        [Test] public void WrongGuidNeverFallsBackToSamePath()
        {
            Write(Bundle()); Assert.AreEqual(AssetBundleSizeCoverage.Unknown, AssetBundleSizeSnapshot.ReadIfPresent(report).Resource("A", new string('b', 32), "Assets/a.asset").Coverage);
        }
        [Test] public void PhysicalCopiesAreSummedAndDuplicateOwnerNamesAreDeduplicated()
        {
            Write(Bundle("A", 20), Bundle("B", 30)); var size = AssetBundleSizeSnapshot.ReadIfPresent(report).Resource(new[] { "A", "B", "A" }, GuidA, "Assets/a.asset");
            Assert.AreEqual(50, size.Bytes); Assert.AreEqual(AssetBundleSizeCoverage.Complete, size.Coverage);
        }
        [Test] public void MissingOwnerMeasurementMakesAggregatePartial()
        {
            var b = Bundle("B"); b.resources[0].guid = new string('b', 32); Write(Bundle(), b);
            var snapshot = AssetBundleSizeSnapshot.ReadIfPresent(report); var size = snapshot.Resource(new[] { "A", "B" }, GuidA, "Assets/a.asset");
            Assert.AreEqual(20, size.Bytes); Assert.AreEqual(AssetBundleSizeCoverage.Partial, size.Coverage); Assert.IsNotEmpty(size.Reason);
        }
        [Test] public void ChangedReportCannotUseStaleSnapshot() { Write(Bundle()); File.AppendAllText(report, " "); Assert.Throws<InvalidDataException>(() => AssetBundleSizeSnapshot.ReadIfPresent(report)); }
        [Test] public void TruncatedSnapshotFailsInsteadOfReturningUnknown() { File.WriteAllText(AssetBundleSizeSnapshot.FilePath(report), "{broken"); Assert.Throws<InvalidDataException>(() => AssetBundleSizeSnapshot.ReadIfPresent(report)); }
        [TestCase("schema")] [TestCase("negative")] [TestCase("sum")] [TestCase("duplicate-bundle")] [TestCase("duplicate-resource")] [TestCase("partial-no-reason")] [TestCase("invalid-guid")]
        public void InvalidExternalSnapshotIsRejected(string defect)
        {
            var b = Bundle(); var data = new BundleSizeFile { schema = 1, reportHash = AssetBundleSizeSnapshot.Hash(report), bundles = new[] { b } };
            switch (defect)
            {
                case "schema": data.schema = 2; break;
                case "negative": b.resources[0].bytes = -1; break;
                case "sum": b.unassignedBytes++; break;
                case "duplicate-bundle": data.bundles = new[] { b, b }; break;
                case "duplicate-resource": b.resources = new[] { b.resources[0], b.resources[0] }; break;
                case "partial-no-reason": b.resources[0].coverage = AssetBundleSizeCoverage.Partial; break;
                case "invalid-guid": b.resources[0].guid = "invalid"; break;
            }
            File.WriteAllText(AssetBundleSizeSnapshot.FilePath(report), JsonUtility.ToJson(data)); Assert.Throws<InvalidDataException>(() => AssetBundleSizeSnapshot.ReadIfPresent(report));
        }
    }
}
