using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using July.Release.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Pipeline.Tasks;
using UnityEditor.Build.Pipeline.Utilities;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace July.Release.Tests
{
    [Category("WebGLAssetBundle")]
    public sealed class BundleSizeBuildTests
    {
        string root, output;
        SpritePackerMode oldPacker;
        SceneSetup[] oldScenes;
        AssetBundleBuild[] builds;
        AssetBundleSizeSnapshot snapshot;
        BundleSizeCollector cold, warm;
        Dictionary<string, string> normal, analyzed, cached, after;
        readonly Dictionary<string, (int Total, int Written)> writes = new Dictionary<string, (int, int)>();
        [Serializable] sealed class BuildTrace { public TraceEvent[] traceEvents; }
        [Serializable] sealed class TraceEvent { public string name; }
        string oldPreference;
        const string Preference = "ProjectSettings/ScriptableBuildPipeline.json";
        [OneTimeSetUp] public void BuildFixtures()
        {
            if (!UnityEditor.BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL)) Assert.Ignore("需要 WebGL 构建模块。");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) Assert.Ignore("构建回归前请保存当前场景。");
            oldScenes = EditorSceneManager.GetSceneManagerSetup(); oldPacker = EditorSettings.spritePackerMode;
            oldPreference = File.Exists(Preference) ? File.ReadAllText(Preference) : null;
            root = "Assets/__JulyBundleSize_" + Guid.NewGuid().ToString("N"); output = Path.GetFullPath("Temp/JulyBundleSize_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); Directory.CreateDirectory(output); AssetDatabase.Refresh();
            EditorSettings.spritePackerMode = SpritePackerMode.BuildTimeOnlyAtlas;
            Fixtures();
            cold = new BundleSizeCollector(); analyzed = Build("analysis", cold);
            normal = Build("normal", null);
            warm = new BundleSizeCollector(); cached = Build("warm", warm);
            after = Build("after", null);
            var report = Path.Combine(output, "fixture.report"); File.WriteAllText(report, "{\"buildFixture\":true}"); AssetBundleSizeSnapshot.Write(report, cold.Bundles); snapshot = AssetBundleSizeSnapshot.ReadIfPresent(report);
        }
        [OneTimeTearDown] public void Cleanup()
        {
            if (root == null) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(root); EditorSettings.spritePackerMode = oldPacker;
            if (oldScenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(oldScenes);
            Assert.That(output, Does.StartWith(Path.GetFullPath("Temp") + Path.DirectorySeparatorChar)); Directory.Delete(output, true);
        }
        AssetBundleResourceSize Size(string bundle, string file) => snapshot.Resource(bundle, AssetDatabase.AssetPathToGUID(root + "/" + file), root + "/" + file);
        Dictionary<string, string> Build(string name, BundleSizeCollector collector)
        {
            var directory = Path.Combine(output, name);
            var parameters = new BundleBuildParameters(BuildTarget.WebGL, BuildTargetGroup.WebGL, directory) { BundleCompression = UnityEngine.BuildCompression.LZ4, UseCache = true, WriteLinkXML = true };
            // Each run owns fresh cache keys, including the built-in shader bundle; never purge the project's shared cache.
            var tasks = SBPBuildTasks.Create("shared_unity_shaders_" + AssetDatabase.AssetPathToGUID(root), null).ToList();
            if (collector != null) tasks.Insert(tasks.FindIndex(t => t is ArchiveAndCompressBundles), collector);
            var log = new BuildLog();
            var code = ContentPipeline.BuildAssetBundles(parameters, new BundleBuildContent(builds), out var result, tasks, log);
            Assert.That((int)code, Is.GreaterThanOrEqualTo(0));
            var trace = JsonUtility.FromJson<BuildTrace>(log.FormatForTraceEventProfiler());
            writes.Add(name, (result.WriteResults.Count, trace.traceEvents.Count(e => e.name.StartsWith("Writing ", StringComparison.Ordinal))));
            Debug.Log($"[BundleSizeBuildTests] {name}: {writes[name].Written}/{writes[name].Total} files freshly written.");
            return result.BundleInfos.ToDictionary(b => b.Key, b => Hash(b.Value.FileName) + "|" + string.Join(",", b.Value.Dependencies.OrderBy(x => x)), StringComparer.Ordinal);
        }
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
        AssetBundleBuild Bundle(string name, params string[] files) => new AssetBundleBuild { assetBundleName = name, assetNames = files.Select(f => root + "/" + f).ToArray() };
        [Test] public void StatisticsDoNotChangeBundleBytesDependenciesOrFollowingNormalBuild()
        { CollectionAssert.AreEquivalent(normal, analyzed); CollectionAssert.AreEquivalent(normal, cached); CollectionAssert.AreEquivalent(normal, after); }
        [Test] public void WarmCacheGivesSamePerResourceStatistics()
        {
            Assert.AreEqual(JsonUtility.ToJson(new BundleSizeFile { bundles = cold.Bundles }), JsonUtility.ToJson(new BundleSizeFile { bundles = warm.Bundles }));
        }
        [Test] public void FirstAnalysisWritesEveryFileAndWarmAnalysisReusesEveryFile()
        {
            Assert.Greater(writes["analysis"].Total, 0);
            Assert.AreEqual(writes["analysis"].Total, writes["analysis"].Written, "First analysis must exercise uncached serialization for every write operation.");
            Assert.AreEqual(writes["analysis"].Total, writes["warm"].Total);
            Assert.AreEqual(0, writes["warm"].Written, "Warm analysis must read every serialized file from the SBP cache.");
        }
        [Test] public void StatisticsNeverModifySbpPreference()
        { Assert.AreEqual(oldPreference, File.Exists(Preference) ? File.ReadAllText(Preference) : null); }
        [Test] public void AudioIncludesBothPayloadsAndKeepsTheirIndividualSizes()
        {
            Assert.AreEqual(49745, Size("audio", "stream.wav").Bytes); Assert.AreEqual(7155, Size("audio", "short.wav").Bytes);
            Assert.AreEqual(AssetBundleSizeCoverage.Complete, Size("audio", "stream.wav").Coverage);
        }
        [Test] public void AtlasIncludesGeneratedPixelsWithoutChargingWholeAtlasToEachSprite()
        {
            var atlas = Size("atlas", "Sprites.spriteatlas"); var sprite = Size("atlas", "sprite1.png");
            Assert.AreEqual(AssetBundleSizeCoverage.Complete, atlas.Coverage); Assert.Greater(atlas.Bytes, 65536); Assert.Greater(atlas.Bytes, 2 * sprite.Bytes);
        }
        [Test] public void MainAndSubMeshAreBothCounted()
        { Assert.AreEqual(2 * Size("mesh-single", "single.asset").Bytes, Size("mesh-double", "double.asset").Bytes); }
        [Test] public void MeshBlendShapeArrayHonorsNestedFieldAlignment()
        {
            var size = Size("blend", "blend.asset");
            Assert.AreEqual(AssetBundleSizeCoverage.Complete, size.Coverage, size.Reason);
            Assert.Greater(size.Bytes, Size("mesh-single", "single.asset").Bytes);
        }
        [Test] public void PrefabOwnBytesExcludeMeshTextureAndMaterialDependencies()
        { Assert.Less(Size("prefab-a", "modelA.prefab").Bytes, 1000); Assert.Greater(Size("prefab-a", "implicit.png").Bytes, 65536); }
        [Test] public void ImplicitTextureCopiedIntoTwoBundlesKeepsSeparatePhysicalSizes()
        { Assert.AreEqual(Size("prefab-a", "implicit.png").Bytes, Size("prefab-b", "implicit.png").Bytes); Assert.Greater(Size("prefab-b", "implicit.png").Bytes, 65536); }
        [Test] public void EachSceneInOneBundleRetainsItsOwnWrittenData()
        {
            foreach (var scene in new[] { "sceneA.unity", "sceneB.unity" }) { var size = Size("scenes", scene); Assert.AreEqual(AssetBundleSizeCoverage.Complete, size.Coverage, scene); Assert.Greater(size.Bytes, 0); }
        }
        [Test] public void EveryPhysicalByteHasAnAssetGeneratedOrUnassignedBucket()
        { foreach (var b in cold.Bundles) Assert.AreEqual(b.unpackedBytes, b.resources.Sum(r => r.bytes) + b.generated.Sum(g => g.bytes) + b.unassignedBytes, b.name); }
        void Fixtures()
        {
            Texture("raw", 256, false, TextureImporterFormat.RGBA32); Texture("compressed", 256, false, TextureImporterFormat.ASTC_6x6); Texture("implicit", 256, false, TextureImporterFormat.RGBA32);
            Texture("sprite0", 64, true, TextureImporterFormat.RGBA32); Texture("sprite1", 64, true, TextureImporterFormat.RGBA32);
            var atlas = new SpriteAtlas(); atlas.SetIncludeInBuild(true);
            atlas.SetPackingSettings(new SpriteAtlasPackingSettings { padding = 4, enableRotation = false, enableTightPacking = false });
            atlas.SetTextureSettings(new SpriteAtlasTextureSettings { generateMipMaps = false, readable = false });
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings { name = "WebGL", overridden = true, maxTextureSize = 1024, format = TextureImporterFormat.RGBA32 });
            atlas.Add(new Object[] { AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/sprite0.png"), AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/sprite1.png") }); AssetDatabase.CreateAsset(atlas, root + "/Sprites.spriteatlas");
            var mesh = new Mesh { name = "single", vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } }; mesh.RecalculateNormals(); AssetDatabase.CreateAsset(mesh, root + "/single.asset");
            var two = Object.Instantiate(mesh); two.name = "double"; AssetDatabase.CreateAsset(two, root + "/double.asset"); var sub = Object.Instantiate(mesh); sub.name = "single"; AssetDatabase.AddObjectToAsset(sub, two);
            var blend = Object.Instantiate(mesh); blend.name = "blend";
            for (int i = 0; i < 2; i++) blend.AddBlendShapeFrame("Shape" + i, 100, new[] { Vector3.up * (i + 1), Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
            AssetDatabase.CreateAsset(blend, root + "/blend.asset");
            for (int i = 0; i < 2; i++)
            {
                var mat = new Material(Shader.Find("Unlit/Texture")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/implicit.png") }; AssetDatabase.CreateAsset(mat, root + "/mat" + i + ".mat");
                var go = new GameObject("Model"); go.AddComponent<MeshFilter>().sharedMesh = two; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                PrefabUtility.SaveAsPrefabAsset(go, root + (i == 0 ? "/modelA.prefab" : "/modelB.prefab")); Object.DestroyImmediate(go);
            }
            Audio("stream", 88200); Audio("short", 11025);
            for (int i = 0; i < 2; i++)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(root + (i == 0 ? "/modelA.prefab" : "/modelB.prefab")));
                new GameObject("Sprite").AddComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(root + "/sprite0.png"); EditorSceneManager.SaveScene(scene, root + (i == 0 ? "/sceneA.unity" : "/sceneB.unity"));
            }
            AssetDatabase.SaveAssets();
            builds = new[] { Bundle("rgba", "raw.png"), Bundle("compressed", "compressed.png"), Bundle("atlas", "Sprites.spriteatlas", "sprite0.png", "sprite1.png"), Bundle("audio", "stream.wav", "short.wav"), Bundle("mesh-single", "single.asset"), Bundle("mesh-double", "double.asset"), Bundle("blend", "blend.asset"), Bundle("prefab-a", "modelA.prefab"), Bundle("prefab-b", "modelB.prefab"), Bundle("scenes", "sceneA.unity", "sceneB.unity") };
        }
        void Texture(string name, int size, bool sprite, TextureImporterFormat format)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false); var pixels = new Color32[size * size]; uint random = 12345;
            for (int i = 0; i < pixels.Length; i++) { random = random * 1664525 + 1013904223; pixels[i] = new Color32((byte)random, (byte)(random >> 8), (byte)(random >> 16), 255); }
            texture.SetPixels32(pixels); texture.Apply(); string path = root + "/" + name + ".png"; File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default; importer.mipmapEnabled = false;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "WebGL", overridden = true, maxTextureSize = 1024, format = format }); importer.SaveAndReimport();
        }
        void Audio(string name, int samples)
        {
            string path = root + "/" + name + ".wav";
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / 44100) * 15000));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); var importer = (AudioImporter)AssetImporter.GetAtPath(path); var settings = importer.defaultSampleSettings; settings.loadType = AudioClipLoadType.Streaming; settings.compressionFormat = AudioCompressionFormat.Vorbis; importer.defaultSampleSettings = settings; importer.SaveAndReimport();
        }
    }
}
