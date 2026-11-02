using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using July.Release.Editor;
using FileData = July.Release.Editor.BundleSizeCollector.WrittenFile;
using ObjectData = July.Release.Editor.BundleSizeCollector.WrittenObject;

namespace July.Release.Tests
{
    public sealed class BundleSizeAttributionTests
    {
        const string First = "11111111111111111111111111111111", Second = "22222222222222222222222222222222";
        static FileData File(string alias, long bytes = 1000, string bundle = "A") => new FileData { Alias = alias, Bundle = bundle, Bytes = bytes };
        static ObjectData Obj(string file, long id, long bytes, string guid = First, string path = "Assets/a.asset", string bundle = "A") => new ObjectData
        { Bundle = bundle, Guid = guid, Path = path, Data = new SerializedBundleData.ObjectData { File = file, Id = id, Offset = (id - 1) * 200, Bytes = bytes, Type = "Fixture" } };
        static void Stream(ObjectData obj, string file, long offset, long bytes) => obj.Data.Streams.Add(new SerializedBundleData.StreamRange { File = file, Offset = offset, Bytes = bytes });
        static void Atlas(ObjectData obj, string file, long id) => obj.Data.AtlasTextures.Add(new SerializedBundleData.ObjectReference { File = file, Id = id });

        [Test] public void SourceAggregatesSubobjectsWithoutAddingIndependentDependencies()
        {
            var result = BundleSizeCollector.Attribute(new[] { File("data") }, new[] { Obj("data", 1, 20), Obj("data", 2, 30), Obj("data", 3, 80, Second, "Assets/dependency.asset") }).Single();
            Assert.AreEqual(50, result.resources.Single(r => r.guid == First).bytes);
            Assert.AreEqual(80, result.resources.Single(r => r.guid == Second).bytes);
            Assert.AreEqual(870, result.unassignedBytes);
        }
        [Test] public void SameGuidCopiedIntoDifferentBundlesKeepsBothPhysicalCopies()
        {
            var result = BundleSizeCollector.Attribute(new[] { File("a"), File("b", bundle: "B") }, new[] { Obj("a", 1, 20), Obj("b", 1, 30, bundle: "B") });
            Assert.AreEqual(20, result.Single(b => b.name == "A").resources.Single().bytes);
            Assert.AreEqual(30, result.Single(b => b.name == "B").resources.Single().bytes);
        }
        [Test] public void GeneratedAtlasTextureIsAssignedOnceAcrossRepeatedSpriteReferencesAndFiles()
        {
            var atlas = Obj("atlas", 1, 304); var texture = Obj("texture", 2, 65696, null, null);
            Atlas(atlas, "texture", 2); Atlas(atlas, "texture", 2);
            var result = BundleSizeCollector.Attribute(new[] { File("atlas"), File("texture", 70000) }, new[] { atlas, texture }).Single();
            Assert.AreEqual(66000, result.resources.Single().bytes); Assert.IsEmpty(result.generated);
        }
        [Test] public void IndependentTextureRemainsItsOwnResourceWhenReferencedByAtlas()
        {
            var atlas = Obj("data", 1, 100); var texture = Obj("data", 2, 200, Second, "Assets/texture.asset"); Atlas(atlas, "data", 2);
            var result = BundleSizeCollector.Attribute(new[] { File("data") }, new[] { atlas, texture }).Single();
            Assert.AreEqual(100, result.resources.Single(r => r.guid == First).bytes); Assert.AreEqual(200, result.resources.Single(r => r.guid == Second).bytes);
        }
        [Test] public void SharedGeneratedTextureIsNotChargedToEveryAtlas()
        {
            var a = Obj("data", 1, 100); var b = Obj("data", 2, 200, Second, "Assets/b.asset"); var texture = Obj("data", 3, 500, null, null);
            Atlas(a, "data", 3); Atlas(b, "data", 3);
            var result = BundleSizeCollector.Attribute(new[] { File("data") }, new[] { a, b, texture }).Single();
            Assert.AreEqual(500, result.generated.Single().bytes); Assert.AreEqual(300, result.resources.Sum(r => r.bytes));
            Assert.IsTrue(result.resources.All(r => r.coverage == AssetBundleSizeCoverage.Partial && r.reason.Contains("共享")));
        }
        [Test] public void GeneratedTextureInDependencyBundleStaysInItsPhysicalBundle()
        {
            var atlas = Obj("a", 1, 100); var texture = Obj("b", 2, 300, null, null, "B"); Atlas(atlas, "b", 2);
            var result = BundleSizeCollector.Attribute(new[] { File("a"), File("b", bundle: "B") }, new[] { atlas, texture });
            Assert.AreEqual(100, result.Single(b => b.name == "A").resources.Single().bytes);
            Assert.AreEqual(AssetBundleSizeCoverage.Partial, result.Single(b => b.name == "A").resources.Single().coverage);
            Assert.AreEqual(300, result.Single(b => b.name == "B").generated.Single().bytes);
        }
        [Test] public void MissingGeneratedTextureIsExplicitlyPartial()
        {
            var atlas = Obj("data", 1, 100); Atlas(atlas, "missing", 2);
            var result = BundleSizeCollector.Attribute(new[] { File("data") }, new[] { atlas }).Single().resources.Single();
            Assert.AreEqual(AssetBundleSizeCoverage.Partial, result.coverage); StringAssert.Contains("未在本次构建找到", result.reason);
        }
        [Test] public void AudioStreamRangesAreAddedPerClipInsteadOfChargingWholeResourceFile()
        {
            var a = Obj("data", 1, 160); var b = Obj("data", 2, 160, Second, "Assets/b.wav");
            Stream(a, "audio", 0, 49585); Stream(b, "audio", 49585, 6995);
            var result = BundleSizeCollector.Attribute(new[] { File("data"), File("audio", 56580) }, new[] { a, b }).Single();
            Assert.AreEqual(49745, result.resources.Single(r => r.guid == First).bytes); Assert.AreEqual(7155, result.resources.Single(r => r.guid == Second).bytes);
        }
        [Test] public void OverlappingRangesOfOneSourceAreCountedAsPhysicalUnion()
        {
            var a = Obj("data", 1, 10); var b = Obj("data", 2, 20); Stream(a, "raw", 0, 100); Stream(b, "raw", 50, 100);
            var result = BundleSizeCollector.Attribute(new[] { File("data"), File("raw", 150) }, new[] { a, b }).Single();
            Assert.AreEqual(180, result.resources.Single().bytes); Assert.IsEmpty(result.generated);
        }
        [Test] public void SharedPartOfStreamIsCountedOnceAndMarkedPartialForBothSources()
        {
            var a = Obj("data", 1, 10); var b = Obj("data", 2, 20, Second, "Assets/b.asset"); Stream(a, "raw", 0, 100); Stream(b, "raw", 50, 100);
            var result = BundleSizeCollector.Attribute(new[] { File("data"), File("raw", 150) }, new[] { a, b }).Single();
            Assert.AreEqual(50, result.generated.Single().bytes); Assert.AreEqual(60, result.resources.Single(r => r.guid == First).bytes); Assert.AreEqual(70, result.resources.Single(r => r.guid == Second).bytes);
            Assert.IsTrue(result.resources.All(r => r.coverage == AssetBundleSizeCoverage.Partial));
        }
        [TestCase(-1, 10)] [TestCase(95, 10)]
        public void OutOfFileStreamRangeFails(long offset, long bytes)
        {
            var a = Obj("data", 1, 10); Stream(a, "raw", offset, bytes);
            Assert.Throws<InvalidDataException>(() => BundleSizeCollector.Attribute(new[] { File("data"), File("raw", 100) }, new[] { a }));
        }
        [Test] public void MissingStreamFileFailsWithAnObservableReason()
        {
            var a = Obj("data", 1, 10); Stream(a, "missing", 0, 10);
            StringAssert.Contains("未在本次写出结果中找到", Assert.Throws<InvalidDataException>(() => BundleSizeCollector.Attribute(new[] { File("data") }, new[] { a })).Message);
        }
        [Test] public void UnsupportedLayoutKeepsKnownBytesAndReasonWithoutClaimingComplete()
        {
            var a = Obj("data", 1, 10); a.Data.Note = "未支持的布局";
            var result = BundleSizeCollector.Attribute(new[] { File("data") }, new[] { a }).Single().resources.Single();
            Assert.AreEqual(10, result.bytes); Assert.AreEqual(AssetBundleSizeCoverage.Partial, result.coverage); Assert.AreEqual("未支持的布局", result.reason);
        }
        [Test] public void StreamOverlappingItsOwnSerializedObjectIsNotCountedTwice()
        {
            var a = Obj("data", 1, 100); Stream(a, "data", 50, 100);
            var result = BundleSizeCollector.Attribute(new[] { File("data") }, new[] { a }).Single();
            Assert.AreEqual(150, result.resources.Single().bytes); Assert.AreEqual(850, result.unassignedBytes);
        }
        [Test] public void StreamOverlappingAnotherObjectIsCountedOnceAsSharedData()
        {
            var a = Obj("data", 1, 100); var b = Obj("data", 2, 100, Second, "Assets/b.asset"); Stream(b, "data", 50, 50);
            var result = BundleSizeCollector.Attribute(new[] { File("data") }, new[] { a, b }).Single();
            Assert.AreEqual(50, result.generated.Single().bytes); Assert.AreEqual(150, result.resources.Sum(r => r.bytes)); Assert.AreEqual(800, result.unassignedBytes);
            Assert.IsTrue(result.resources.All(r => r.coverage == AssetBundleSizeCoverage.Partial));
        }
        [Test] public void RepeatedSharedSegmentsKeepOneReasonPerResource()
        {
            var a = Obj("data", 1, 100); var b = Obj("data", 2, 100, Second, "Assets/b.asset"); Stream(b, "data", 0, 30); Stream(b, "data", 30, 30);
            var result = BundleSizeCollector.Attribute(new[] { File("data") }, new[] { a, b }).Single();
            Assert.IsTrue(result.resources.All(r => r.reason.Split('；').Length == 1)); Assert.AreEqual(60, result.generated.Single().bytes);
        }
    }
}
