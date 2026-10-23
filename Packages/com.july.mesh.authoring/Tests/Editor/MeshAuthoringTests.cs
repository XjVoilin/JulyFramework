using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring.Tests
{
    public sealed class MeshAuthoringTests
    {
        const string Folder = "Assets/__JulyMeshAuthoringTests";
        readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { temporary.Add(value); return value; }

        [SetUp] public void SetUp() { MeshAssetEdits.EnsureFolder(Folder); }
        [TearDown] public void TearDown()
        {
            foreach (var value in temporary) if (value && !EditorUtility.IsPersistent(value)) UnityEngine.Object.DestroyImmediate(value);
            temporary.Clear();
            AssetDatabase.DeleteAsset(Folder);
        }

        static UMesh Grid(bool curved = false, int size = 24)
        {
            var mesh = new UMesh { name = curved ? "Curved" : "Plane" };
            var vertices = new List<Vector3>(); var uvs = new List<Vector4>(); var colors = new List<Color>(); var indices = new List<int>();
            for (int y = 0; y <= size; y++) for (int x = 0; x <= size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                vertices.Add(new Vector3(u, curved ? (u * u + v * v) * 2 : 0, v));
                uvs.Add(new Vector4(u, v, u + v, u - v)); colors.Add(new Color(u, v, .5f, 1));
            }
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                int i = y * (size + 1) + x;
                indices.AddRange(new[] { i, i + size + 1, i + 1, i + 1, i + size + 1, i + size + 2 });
            }
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetUVs(1, uvs); mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        UMesh AssetMesh(string name = "Mesh")
        {
            var mesh = Grid(); mesh.name = name; AssetDatabase.CreateAsset(mesh, Folder + "/" + name + ".asset"); return mesh;
        }
        static MeshReduction Reduce(MeshGeometry geometry, float ratio = 100, bool automatic = true, int fidelity = 0) => MeshReduction.Calculate(geometry, ratio, automatic, fidelity, CancellationToken.None);

        [Test] public void AutomaticAndManualUseDifferentStoppingRules()
        {
            var mesh = Own(Grid(true)); var geometry = MeshGeometry.Read(mesh);
            var automatic = Reduce(geometry); var manual = Reduce(geometry, 15, false);
            Assert.That(automatic.Triangles, Is.LessThan(geometry.Triangles));
            Assert.That(manual.Triangles, Is.LessThan(automatic.Triangles));
            var plane = Own(Grid());
            Assert.That(Reduce(MeshGeometry.Read(plane)).Triangles, Is.LessThan(automatic.Triangles));
        }

        [Test] public void CompactResultPreservesAllVertexChannelsAndUvDimensions()
        {
            var mesh = Own(Grid()); var geometry = MeshGeometry.Read(mesh);
            var reduced = Own(geometry.Build(Reduce(geometry, 40, false).Submeshes));
            Assert.That(reduced.vertexCount, Is.LessThan(mesh.vertexCount));
            Assert.That(reduced.GetVertexAttributes(), Is.EqualTo(mesh.GetVertexAttributes()));
            var sourceUv = new List<Vector4>(); var resultUv = new List<Vector4>();
            mesh.GetUVs(1, sourceUv); reduced.GetUVs(1, resultUv);
            var positions = mesh.vertices.Select((value,index) => (value,index)).ToDictionary(pair => pair.value, pair => pair.index);
            var normals = mesh.normals; var colors = mesh.colors; var tangents = mesh.tangents;
            for (int i = 0; i < reduced.vertexCount; i++)
            {
                int original = positions[reduced.vertices[i]];
                Assert.That(resultUv[i], Is.EqualTo(sourceUv[original]));
                Assert.That(reduced.normals[i], Is.EqualTo(normals[original]));
                Assert.That(reduced.colors[i], Is.EqualTo(colors[original]));
                Assert.That(reduced.tangents[i], Is.EqualTo(tangents[original]));
            }
        }

        [Test] public void EditorCanReadNonReadableMeshWithoutChangingReadWrite()
        {
            var mesh = Own(Grid()); mesh.UploadMeshData(true);
            Assert.That(mesh.isReadable, Is.False);
            var geometry = MeshGeometry.Read(mesh);
            Assert.That(geometry.Triangles, Is.EqualTo(1152));
            Assert.That(mesh.isReadable, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ApplyPreservesReadWriteAcrossRepeatedEditsAndReload(bool readable)
        {
            var mesh = AssetMesh("ReadWriteApply");
            SetReadWrite(mesh, readable);
            mesh = Reload(mesh);
            string identity = MeshAssetEdits.Identity(mesh);
            string baseline = MeshGeometry.Read(mesh).Fingerprint();
            foreach (float ratio in new[] { 60f, 30f })
            {
                string expected;
                using (var row = new MeshRow { Source = mesh })
                {
                    row.Load();
                    Assert.That(row.BaselineHash, Is.EqualTo(baseline));
                    row.Ratio = ratio; row.Automatic = false;
                    row.SetCandidate(Reduce(row.Geometry, ratio, false));
                    expected = row.CandidateHash;
                    MeshAssets.Apply(row, 0);
                    Assert.That(mesh.isReadable, Is.EqualTo(readable), "应用必须保持目标的 Read/Write。");
                }
                mesh = Reload(mesh);
                Assert.That(mesh.isReadable, Is.EqualTo(readable), "保存重载后也必须保持设置。");
                Assert.That(MeshAssetEdits.Identity(mesh), Is.EqualTo(identity));
                Assert.That(MeshGeometry.Read(mesh).Fingerprint(), Is.EqualTo(expected));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestorePreservesCurrentReadWriteInsteadOfCachedBaselineSetting(bool readable)
        {
            var mesh = AssetMesh("ReadWriteRestore");
            SetReadWrite(mesh, !readable);
            mesh = Reload(mesh);
            string baseline = MeshGeometry.Read(mesh).Fingerprint();
            string identity = MeshAssetEdits.Identity(mesh);
            using (var row = new MeshRow { Source = mesh })
            {
                row.Load(); row.Ratio = 40; row.Automatic = false;
                row.SetCandidate(Reduce(row.Geometry, 40, false));
                MeshAssets.Apply(row, 0);
                // 用户在减面后改变发布设置，还原几何时应保持当前选择。
                SetReadWrite(mesh, readable);
                MeshAssets.Restore(row);
                Assert.That(mesh.isReadable, Is.EqualTo(readable), "还原不能写回缓存基准的 Read/Write。");
            }
            mesh = Reload(mesh);
            Assert.That(mesh.isReadable, Is.EqualTo(readable));
            Assert.That(MeshAssetEdits.Identity(mesh), Is.EqualTo(identity));
            Assert.That(MeshGeometry.Read(mesh).Fingerprint(), Is.EqualTo(baseline));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedWritePreservesGeometryAndReadWrite(bool readable)
        {
            var mesh = AssetMesh("ReadWriteRollback");
            SetReadWrite(mesh, readable);
            mesh = Reload(mesh);
            string baseline = MeshGeometry.Read(mesh).Fingerprint();
            string identity = MeshAssetEdits.Identity(mesh);
            using (var row = new MeshRow { Source = mesh })
            {
                row.Load(); row.Ratio = 40; row.Automatic = false;
                row.SetCandidate(Reduce(row.Geometry, 40, false));
                row.CandidateHash = "invalid expected geometry hash";
                var error = Assert.Throws<InvalidOperationException>(() => MeshAssets.Apply(row, 0));
                Assert.That(error.Message, Does.Contain("写入后的 Mesh 与预览不一致"));
                Assert.That(mesh.isReadable, Is.EqualTo(readable));
            }
            mesh = Reload(mesh);
            Assert.That(mesh.isReadable, Is.EqualTo(readable));
            Assert.That(MeshAssetEdits.Identity(mesh), Is.EqualTo(identity));
            Assert.That(MeshGeometry.Read(mesh).Fingerprint(), Is.EqualTo(baseline));
        }

        static void SetReadWrite(UMesh mesh, bool readable)
        {
            using (var serialized = new SerializedObject(mesh))
            {
                serialized.FindProperty("m_IsReadable").boolValue = readable;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
        }

        static UMesh Reload(UMesh mesh)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            Resources.UnloadAsset(mesh);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<UMesh>(path);
        }

        [Test] public void SubmeshOrderAndSharedBoundarySurvive()
        {
            var mesh = Own(Grid()); int[] indices = mesh.triangles;
            mesh.subMeshCount = 3;
            int split = indices.Length / 2;
            mesh.SetTriangles(indices.Take(split).ToArray(), 0);
            mesh.SetTriangles(Array.Empty<int>(), 1);
            mesh.SetTriangles(indices.Skip(split).ToArray(), 2);
            var geometry = MeshGeometry.Read(mesh);
            var result = Reduce(geometry, 20, false);
            var compact = Own(geometry.Build(result.Submeshes));
            Assert.That(compact.subMeshCount, Is.EqualTo(3));
            Assert.That(compact.GetIndexCount(1), Is.Zero);
            foreach (var p in mesh.vertices.Where(v => Mathf.Abs(v.z - .5f) < .00001f))
                Assert.That(compact.vertices, Does.Contain(p));
        }

        [Test] public void InPlaceApplyAndRestoreRefreshPreviouslyRenderedMesh()
        {
            var mesh = Grid(true); AssetDatabase.CreateAsset(mesh, Folder + "/Rendered.asset");
            var original = Own(UnityEngine.Object.Instantiate(mesh));
            using (var preview = new MeshThumbnails())
            using (var row = new MeshRow { Source = mesh })
            {
                row.Load();
                var originalImage = Own(preview.Render(mesh, row.Geometry.Bounds, 320, 240));
                foreach (float ratio in new[] { 60f, 30f, 85f })
                {
                    row.Ratio = ratio; row.Automatic = false;
                    row.SetCandidate(Reduce(row.Geometry, ratio, false));
                    var expected = Own(preview.Render(row.Candidate, row.Geometry.Bounds, 320, 240));
                    MeshAssets.Apply(row, 0);
                    var actual = Own(preview.Render(mesh, row.Geometry.Bounds, 320, 240));
                    Assert.That(DifferentPixels(expected, actual), Is.LessThan(.01f), $"应用 {ratio}% 后，已显示过的 Mesh 必须与候选绘制一致。");
                }
                MeshAssets.Restore(row);
                var restored = Own(preview.Render(mesh, row.Geometry.Bounds, 320, 240));
                Assert.That(DifferentPixels(originalImage, restored), Is.LessThan(.01f), "恢复后必须同步更新已使用的绘制数据。");
            }
        }

        static float DifferentPixels(Texture2D expected, Texture2D actual)
        {
            var a = expected.GetPixels32(); var b = actual.GetPixels32(); int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i].r - b[i].r) > 3 || Math.Abs(a[i].g - b[i].g) > 3 || Math.Abs(a[i].b - b[i].b) > 3) changed++;
            return (float)changed / a.Length;
        }

        [Test] public void BuiltSubmeshesDescribeReferencedVertexRangesAndBounds()
        {
            var source = Own(Grid(true)); var indices = source.triangles;
            source.subMeshCount = 3;
            source.SetTriangles(indices.Take(indices.Length / 2).ToArray(), 0);
            source.SetTriangles(Array.Empty<int>(), 1);
            source.SetTriangles(indices.Skip(indices.Length / 2).ToArray(), 2);
            var geometry = MeshGeometry.Read(source);
            var mesh = Own(geometry.Build(Reduce(geometry, 50, false).Submeshes));
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var descriptor = mesh.GetSubMesh(sub); var used = mesh.GetIndices(sub);
                if (used.Length == 0) { Assert.That(descriptor.vertexCount, Is.Zero); continue; }
                Assert.That(descriptor.firstVertex, Is.EqualTo(used.Min()));
                Assert.That(descriptor.vertexCount, Is.EqualTo(used.Max() - used.Min() + 1));
                var bounds = descriptor.bounds; bounds.Expand(.00001f);
                foreach (int vertex in used) Assert.That(bounds.Contains(mesh.vertices[vertex]), Is.True, "子网格包围盒必须覆盖引用顶点。");
            }
            Assert.That(mesh.bounds, Is.EqualTo(source.bounds), "整体预览取景保持原始范围。");
        }

        [Test] public void StandaloneChangesAlwaysUseOriginalAndRestoreIdentity()
        {
            var mesh = AssetMesh(); string identity = MeshAssetEdits.Identity(mesh), original = MeshGeometry.Read(mesh).Fingerprint();
            using (var row = new MeshRow { Source = mesh })
            {
                row.Load();
                foreach (float ratio in new[] { 90f, 60f, 85f })
                {
                    Assert.That(row.BaselineHash, Is.EqualTo(original));
                    row.Ratio = ratio; row.Automatic = false; row.SetCandidate(Reduce(row.Geometry, ratio, false));
                    MeshAssets.Apply(row, 0); Assert.That(MeshAssetEdits.Identity(mesh), Is.EqualTo(identity));
                }
                MeshAssets.Restore(row);
                Assert.That(MeshGeometry.Read(mesh).Fingerprint(), Is.EqualTo(original));
                Assert.That(MeshAssetEdits.Identity(mesh), Is.EqualTo(identity));
            }
        }

        [Test] public void ExternalStandaloneChangesAreNotOverwritten()
        {
            var mesh = AssetMesh();
            using (var row = new MeshRow { Source = mesh })
            {
                row.Load(); row.Ratio = 50; row.Automatic = false; row.SetCandidate(Reduce(row.Geometry, 50, false)); MeshAssets.Apply(row, 0);
                var positions = mesh.vertices; positions[0] += Vector3.up; mesh.vertices = positions;
                string changed = MeshGeometry.Read(mesh).Fingerprint();
                Assert.Throws<InvalidOperationException>(() => MeshAssets.Restore(row));
                Assert.That(MeshGeometry.Read(mesh).Fingerprint(), Is.EqualTo(changed));
            }
        }

        [Test] public void ColliderSharingBlocksInPlaceChanges()
        {
            var mesh = AssetMesh(); var root = Own(new GameObject("ColliderConsumer")); root.AddComponent<MeshCollider>().sharedMesh = mesh;
            using (var row = new MeshRow { Source = mesh })
            {
                row.Load(); row.Ratio = 50; row.Automatic = false; row.SetCandidate(Reduce(row.Geometry, 50, false));
                Assert.Throws<InvalidOperationException>(() => MeshAssets.Apply(row, 0));
                Assert.That(MeshGeometry.Read(mesh).Fingerprint(), Is.EqualTo(row.ObservedHash));
            }
        }

        UMesh ImportedSource()
        {
            // Real FBX fixture copied by the task harness; package tests remain usable via an OBJ fallback.
            string fixture = "Assets/JulyMeshSamples/Source.fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(fixture)) AssetDatabase.CopyAsset(fixture, Folder + "/Source.fbx");
            else
            {
                System.IO.File.WriteAllText(Folder + "/Source.obj", "v 0 0 0\nv 1 0 0\nv 0 0 1\nv 1 0 1\nf 1 3 2\nf 2 3 4\n");
                AssetDatabase.ImportAsset(Folder + "/Source.obj");
            }
            string path = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Source.fbx") ? Folder + "/Source.fbx" : Folder + "/Source.obj";
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<UMesh>().First();
        }

        [Test] public void StandaloneMeshAppliesToAllConsumersWithoutReplacingReferences()
        {
            var source = AssetMesh("Shared"); string path = AssetDatabase.GetAssetPath(source), bytes = Convert.ToBase64String(System.IO.File.ReadAllBytes(path));
            var go = Own(new GameObject("Consumer")); go.AddComponent<MeshFilter>().sharedMesh = source;
            var first = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/First.prefab");
            var second = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Second.prefab");
            using (var row = new MeshRow { Source = source })
            {
                row.Load(); string original = row.BaselineHash, identity = MeshAssetEdits.Identity(source);
                row.Ratio = 60; row.Automatic = false; row.SetCandidate(Reduce(row.Geometry, 60, false)); string expected = row.CandidateHash;
                MeshAssets.Apply(row, 0);
                foreach (var prefab in new[] { first, second })
                {
                    var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(MeshAssetEdits.Identity(mesh), Is.EqualTo(identity)); Assert.That(MeshGeometry.Read(mesh).Fingerprint(), Is.EqualTo(expected));
                }
                MeshAssets.Restore(row); Assert.That(MeshGeometry.Read(row.Source).Fingerprint(), Is.EqualTo(original));
                Assert.That(Convert.ToBase64String(System.IO.File.ReadAllBytes(path)), Is.EqualTo(bytes));
            }
        }

        [Test] public void RestoreLeavesUnrelatedPrefabOverridesAlone()
        {
            var source = AssetMesh("Source"); var other = AssetMesh("Other"); var go = Own(new GameObject("Consumer")); go.AddComponent<MeshFilter>().sharedMesh = source;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Consumer.prefab"); var filter = prefab.GetComponent<MeshFilter>();
            using (var row = new MeshRow { Source = source })
            {
                row.Load(); row.Ratio = 50; row.Automatic = false; row.SetCandidate(Reduce(row.Geometry, 50, false)); MeshAssets.Apply(row, 0);
                filter.sharedMesh = other; EditorUtility.SetDirty(filter); PrefabUtility.SavePrefabAsset(prefab);
                MeshAssets.Restore(row); Assert.That(filter.sharedMesh, Is.EqualTo(other));
            }
        }

        [Test] public void ImportedMeshIsSkippedAndCannotBeModifiedThroughTheBackend()
        {
            var source = ImportedSource(); string path = AssetDatabase.GetAssetPath(source);
            var before = System.IO.File.ReadAllBytes(path);
            var consumer = Own(new GameObject("ImportedConsumer")); consumer.AddComponent<MeshFilter>().sharedMesh = source;
            var prefab = PrefabUtility.SaveAsPrefabAsset(consumer, Folder + "/Imported.prefab");
            var rows = MeshSelection.Collect(new UnityEngine.Object[] { source, prefab, AssetDatabase.LoadMainAssetAtPath(path) }, out int skipped);
            Assert.That(rows, Is.Empty); Assert.That(skipped, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => MeshAssets.Baseline(source));
            Assert.That(System.IO.File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(prefab.GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(source));
        }

        [Test] public void MixedFolderCollectsOnlyIndependentMeshesAndDeduplicatesPrefabConsumers()
        {
            var imported = ImportedSource(); var standalone = AssetMesh("Extracted");
            var consumer = Own(new GameObject("Consumer")); consumer.AddComponent<MeshFilter>().sharedMesh = standalone;
            PrefabUtility.SaveAsPrefabAsset(consumer, Folder + "/First.prefab");
            PrefabUtility.SaveAsPrefabAsset(consumer, Folder + "/Second.prefab");
            var rows = MeshSelection.Collect(new UnityEngine.Object[] { AssetDatabase.LoadAssetAtPath<DefaultAsset>(Folder) }, out int skipped);
            try { Assert.That(rows.Count, Is.EqualTo(1)); Assert.That(rows[0].Source, Is.EqualTo(standalone)); Assert.That(skipped, Is.EqualTo(1)); }
            finally { foreach (var row in rows) row.Dispose(); }
        }

        [UnityTest] public IEnumerator LatestSliderRequestWinsAndRecommendationIsPreserved()
        {
            var source = AssetMesh();
            using (var workspace = new MeshWorkspace())
            {
                var row = new MeshRow { Source = source }; workspace.Rows.Add(row);
                workspace.Request(row, 100, true);
                for (int i = 0; workspace.Working && i < 5000; i++) { workspace.Tick(); yield return null; }
                Assert.That(row.Error, Is.Null); Assert.That(row.Recommendation, Is.Not.Null);
                int recommended = row.Recommendation.Triangles;
                workspace.Request(row, 15, false); workspace.Tick();
                workspace.Request(row, 90, false);
                for (int i = 0; workspace.Working && i < 5000; i++) { workspace.Tick(); yield return null; }
                Assert.That(row.Error, Is.Null);
                Assert.That(row.CandidateTriangles, Is.EqualTo(Reduce(row.Geometry, 90, false).Triangles));
                Assert.That(row.Recommendation.Triangles, Is.EqualTo(recommended));
                workspace.RestoreRecommendation(row);
                Assert.That(row.CandidateTriangles, Is.EqualTo(recommended));
                Assert.That(row.Automatic, Is.True);
                Assert.That(MeshGeometry.Read(source).Triangles, Is.EqualTo(1152));
            }
        }

        [Test] public void PluginIsEditorOnlyAndWindowCanBeCreated()
        {
            string dll = AssetDatabase.FindAssets("JulyMeshOptimizer").Select(AssetDatabase.GUIDToAssetPath).First(path => path.EndsWith(".dll"));
            var importer = (PluginImporter)AssetImporter.GetAtPath(dll);
            Assert.That(importer.GetCompatibleWithEditor(), Is.True);
            Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.False);
            foreach (string path in AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Packages/com.july.mesh.authoring/Editor/Plugins/", StringComparison.Ordinal)))
            {
                var plugin = AssetImporter.GetAtPath(path) as PluginImporter;
                if (plugin) Assert.That(plugin.GetCompatibleWithAnyPlatform(), Is.False, path);
            }
            var window = EditorWindow.GetWindow<MeshAuthoringWindow>();
            Assert.That(window.rootVisualElement.childCount, Is.GreaterThan(0)); window.Close();
        }

        [Test] public void ThumbnailsRenderUsingTheSameOriginalFrame()
        {
            var mesh = Own(Grid(true)); var geometry = MeshGeometry.Read(mesh);
            var candidate = Own(geometry.Build(Reduce(geometry, 30, false).Submeshes));
            using (var preview = new MeshThumbnails())
            {
                var original = Own(preview.Render(mesh, geometry.Bounds));
                var result = Own(preview.Render(candidate, geometry.Bounds));
                Assert.That(original.width, Is.EqualTo(result.width));
                Assert.That(original.GetPixels32().Distinct().Count(), Is.GreaterThan(2));
                Assert.That(result.GetPixels32().Distinct().Count(), Is.GreaterThan(2));
                Assert.That(original.GetPixels32().All(pixel => Math.Abs(pixel.r - pixel.g) <= 1 && Math.Abs(pixel.g - pixel.b) <= 1), Is.True, "缩略图应使用中性材质，不显示贴图或顶点色。");
            }
        }

        [UnityTest] public IEnumerator PopulatedWindowUpdatesInlineAndResetsUncheckedRows()
        {
            var first = AssetMesh("First"); var second = AssetMesh("Second");
            var beforeSelection = Selection.objects;
            var window = EditorWindow.GetWindow<MeshAuthoringWindow>(); window.position = new Rect(80, 80, 920, 600);
            try
            {
                Selection.objects = new UnityEngine.Object[] { first, second }; window.ReadSelection();
                for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                yield return null; window.Repaint(); yield return null;
                Assert.That(window.Workspace.Rows.Count, Is.EqualTo(2));
                foreach (var row in window.Workspace.Rows) Assert.That(row.Error, Is.Null);
                var selectionSummary = window.rootVisualElement.Q<Label>("mesh-selection-summary");
                long allReduced = window.Workspace.Rows.Sum(row => (long)row.OriginalTriangles - row.CandidateTriangles);
                Assert.That(selectionSummary.text, Is.EqualTo($"已勾选 2 / 2 项 · 预计减少 {allReduced:N0} 面"));
                var fields = window.rootVisualElement.Query<FloatField>().ToList();
                Assert.That(fields.Count, Is.GreaterThan(0), "ListView 未生成实际行。");
                fields[0].value = 15;
                for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                Assert.That(window.Workspace.Rows[0].Automatic, Is.False);
                Assert.That(window.Workspace.Rows[0].Ratio, Is.EqualTo(15));
                var uncheckedRow = window.Workspace.Rows[1]; uncheckedRow.Checked = false;
                window.Workspace.Request(uncheckedRow, 70, false);
                long checkedReduced = (long)window.Workspace.Rows[0].OriginalTriangles - window.Workspace.Rows[0].CandidateTriangles;
                Assert.That(selectionSummary.text, Is.EqualTo($"已勾选 1 / 2 项 · 预计减少 {checkedReduced:N0} 面"));
                var checkedRow = window.Workspace.Rows[0];
                window.Workspace.Request(checkedRow, 20, false);
                Assert.That(selectionSummary.text, Is.EqualTo("已勾选 1 / 2 项 · 预计减少 0 面（1 项未计入）"));
                for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                checkedReduced = (long)checkedRow.OriginalTriangles - checkedRow.CandidateTriangles;
                Assert.That(selectionSummary.text, Is.EqualTo($"已勾选 1 / 2 项 · 预计减少 {checkedReduced:N0} 面"));
                window.RestoreAllRecommendations();
                for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                Assert.That(uncheckedRow.Checked, Is.False);
                foreach (var row in window.Workspace.Rows)
                {
                    Assert.That(row.Automatic, Is.True);
                    Assert.That(row.CandidateTriangles, Is.EqualTo(row.Recommendation.Triangles));
                    Assert.That(MeshGeometry.Read(row.Source).Triangles, Is.EqualTo(1152));
                }
            }
            finally { window.Close(); Selection.objects = beforeSelection; }
        }

        [UnityTest] public IEnumerator SelectAllAndDeselectAllIncludeOffscreenRowsWithoutChangingCandidatesOrOrder()
        {
            var meshes = Enumerable.Range(0, 12).Select(i => AssetMesh("Select" + i)).ToArray();
            var beforeSelection = Selection.objects;
            var window = EditorWindow.GetWindow<MeshAuthoringWindow>(); window.position = new Rect(80, 80, 920, 600);
            void Click(string name)
            {
                var button = window.rootVisualElement.Q<ToolbarButton>(name);
                Assert.That(button, Is.Not.Null);
                using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
            }
            try
            {
                Selection.objects = meshes; window.ReadSelection();
                for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                window.Workspace.Request(window.Workspace.Rows[0], 40, false);
                for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                window.SortBy(MeshListSort.ReducedTriangles);
                yield return null; yield return null;
                var visible = window.rootVisualElement.Query<Toggle>("mesh-check").ToList();
                Assert.That(visible.Count, Is.GreaterThan(0));
                Assert.That(visible.Count, Is.LessThan(meshes.Length), "必须涵盖虚拟列表中不可见的项目。");
                var order = window.DisplayRows.ToArray();
                var ratios = order.Select(row => row.Ratio).ToArray();
                var candidates = order.Select(row => row.Candidate).ToArray();
                var summary = window.rootVisualElement.Q<Label>("mesh-selection-summary");
                Click("mesh-deselect-all"); Click("mesh-deselect-all");
                Assert.That(window.Workspace.Rows.All(row => !row.Checked), Is.True);
                Assert.That(visible.All(toggle => !toggle.value), Is.True);
                Assert.That(summary.text, Is.EqualTo("已勾选 0 / 12 项 · 预计减少 0 面"));
                Click("mesh-select-all"); Click("mesh-select-all");
                Assert.That(window.Workspace.Rows.All(row => row.Checked), Is.True);
                Assert.That(visible.All(toggle => toggle.value), Is.True);
                long reduced = order.Sum(row => (long)row.CurrentTriangles - row.CandidateTriangles);
                Assert.That(summary.text, Is.EqualTo($"已勾选 12 / 12 项 · 预计减少 {reduced:N0} 面"));
                CollectionAssert.AreEqual(order, window.DisplayRows);
                CollectionAssert.AreEqual(ratios, window.DisplayRows.Select(row => row.Ratio));
                CollectionAssert.AreEqual(candidates, window.DisplayRows.Select(row => row.Candidate));
                foreach (var mesh in meshes) Assert.That(MeshGeometry.Read(mesh).Triangles, Is.EqualTo(1152));
                window.Workspace.Clear();
                Click("mesh-select-all"); Click("mesh-deselect-all");
                Assert.That(summary.text, Is.EqualTo("已勾选 0 / 0 项 · 预计减少 0 面"));
            }
            finally { window.Close(); Selection.objects = beforeSelection; }
        }

        [UnityTest] public IEnumerator SortingUsesActualResultsAndKeepsOrderUntilExplicitlyRequested()
        {
            var large = Grid(false, 48); large.name = "Mesh10"; AssetDatabase.CreateAsset(large, Folder + "/Mesh10.asset");
            var small = Grid(false, 12); small.name = "Mesh2"; AssetDatabase.CreateAsset(small, Folder + "/Mesh2.asset");
            var missing = AssetMesh("Waiting");
            var beforeSelection = Selection.objects;
            var window = EditorWindow.GetWindow<MeshAuthoringWindow>(); window.position = new Rect(80, 80, 920, 700);
            try
            {
                Selection.objects = new UnityEngine.Object[] { large, small }; window.ReadSelection();
                Assert.That(window.DisplayRows[0].Source, Is.SameAs(small), "默认应按名称自然排序，Mesh2 排在 Mesh10 前，且不等待候选生成。");
                Assert.That(window.rootVisualElement.Q<Button>("mesh-sort-Name").text, Is.EqualTo("名称  ▲"));
                for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                var largeRow = window.Workspace.Rows.Single(row => row.Source == large);
                var smallRow = window.Workspace.Rows.Single(row => row.Source == small);
                yield return null; yield return null;
                var firstName = window.rootVisualElement.Q<Label>("mesh-name");
                var originalImage = window.rootVisualElement.Query<Image>(className: "mesh-thumbnail").ToList()[0];
                var originalTexture = originalImage.image;
                Assert.That(originalTexture, Is.Not.Null);
                window.Workspace.Request(largeRow, 90, false); window.Workspace.Request(smallRow, 40, false);
                for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                Assert.That(largeRow.Error, Is.Null); Assert.That(smallRow.Error, Is.Null);
                Assert.That(window.rootVisualElement.Q<Label>("mesh-name"), Is.SameAs(firstName), "名称排序下生成结果不应重建可见行。");
                Assert.That(originalImage.image, Is.SameAs(originalTexture), "名称排序下应复用原始缩略图。");
                window.SortBy(MeshListSort.Name);
                Assert.That(window.DisplayRows[0], Is.SameAs(largeRow));
                window.SortBy(MeshListSort.Name);
                Assert.That(window.DisplayRows[0], Is.SameAs(smallRow));
                window.SortBy(MeshListSort.RetainedRatio);
                Assert.That(window.DisplayRows[0], Is.SameAs(smallRow));
                smallRow.Checked = false;
                // Target percentages intentionally differ from the completed candidates.
                largeRow.Ratio = 1; smallRow.Ratio = 99;
                var waiting = new MeshRow { Source = missing, ScopeLabel = "仅生成资源" };
                window.Workspace.Rows.Add(waiting);
                window.SortBy(MeshListSort.ReducedTriangles);
                Assert.That(window.DisplayRows[0], Is.SameAs(largeRow));
                Assert.That(window.DisplayRows.Last(), Is.SameAs(waiting));
                window.SortBy(MeshListSort.ReducedTriangles);
                Assert.That(window.DisplayRows[0], Is.SameAs(smallRow));
                Assert.That(window.DisplayRows.Last(), Is.SameAs(waiting));
                window.SortBy(MeshListSort.RetainedRatio);
                Assert.That(window.DisplayRows[0], Is.SameAs(smallRow));
                Assert.That(smallRow.Checked, Is.False); Assert.That(smallRow.Ratio, Is.EqualTo(99));
                window.Workspace.Rows.Remove(waiting); waiting.Dispose();
                window.SortBy(MeshListSort.RetainedRatio); // Descending.
                Assert.That(window.DisplayRows[0], Is.SameAs(largeRow));
                yield return null; yield return null;
                var field = window.rootVisualElement.Query<FloatField>("mesh-ratio").ToList()[0];
                field.Focus(); yield return null;
                field.value = 30;
                for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                Assert.That(largeRow.ActualRatio, Is.LessThan(smallRow.ActualRatio));
                Assert.That(window.DisplayRows[0], Is.SameAs(largeRow), "输入期间不能重排行。");
                Assert.That(field.value, Is.EqualTo(30));
                window.rootVisualElement.focusController.focusedElement?.Blur();
                yield return null; yield return null; yield return null;
                Assert.That(window.DisplayRows[0], Is.SameAs(largeRow), "输入结束后仍须保持行位置。");
                var slider = window.rootVisualElement.Query<Slider>("mesh-slider").ToList()[0];
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = slider.worldBound.center })) { down.target = slider; slider.SendEvent(down); }
                slider.value = 1;
                for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                Assert.That(largeRow.ActualRatio, Is.LessThan(smallRow.ActualRatio));
                Assert.That(window.DisplayRows[0], Is.SameAs(largeRow), "拖动期间不能重排行。");
                using (var cancel = PointerCancelEvent.GetPooled()) { cancel.target = slider; slider.SendEvent(cancel); }
                yield return null; yield return null; yield return null;
                Assert.That(window.DisplayRows[0], Is.SameAs(largeRow), "拖动结束后仍须保持行位置。");
                window.SortBy(MeshListSort.RetainedRatio); // Ascending, using the latest result.
                Assert.That(window.DisplayRows[0], Is.SameAs(largeRow));
                window.SortBy(MeshListSort.RetainedRatio); // Descending.
                Assert.That(window.DisplayRows[0], Is.SameAs(smallRow), "再次点击排序按钮才根据新结果重排。");
                window.SortBy(MeshListSort.RetainedRatio);
                Assert.That(window.DisplayRows[0], Is.SameAs(largeRow));
                var name = window.rootVisualElement.Query<Label>("mesh-name").ToList()[0];
                Selection.activeObject = small;
                using (var click = ClickEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 })) { click.target = name; name.SendEvent(click); }
                Assert.That(Selection.activeObject, Is.SameAs(large));
                var image = window.rootVisualElement.Query<Image>(className: "mesh-thumbnail").ToList()[0];
                field = window.rootVisualElement.Query<FloatField>("mesh-ratio").ToList()[0];
                double layoutDeadline = EditorApplication.timeSinceStartup + 2;
                while ((float.IsNaN(image.worldBound.xMax) || float.IsNaN(field.worldBound.xMax)) && EditorApplication.timeSinceStartup < layoutDeadline) yield return null;
                Assert.That(image.worldBound.xMax, Is.LessThan(field.worldBound.xMax));
                Selection.activeObject = small;
                using (var click = ClickEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 })) { click.target = image; image.SendEvent(click); }
                Assert.That(Selection.activeObject, Is.SameAs(large));
                window.position = new Rect(80, 80, 460, 700);
                for (int i = 0; !window.rootVisualElement.ClassListContains("mesh-compact") && i < 200; i++) yield return null;
                yield return null; yield return null;
                Assert.That(window.rootVisualElement.ClassListContains("mesh-compact"), Is.True);
                var controls = window.rootVisualElement.Query<VisualElement>(className: "mesh-controls").ToList()[0];
                image = window.rootVisualElement.Query<Image>(className: "mesh-thumbnail").ToList()[0];
                layoutDeadline = EditorApplication.timeSinceStartup + 2;
                while ((float.IsNaN(controls.worldBound.yMin) || float.IsNaN(image.worldBound.yMax)) && EditorApplication.timeSinceStartup < layoutDeadline) yield return null;
                Assert.That(controls.worldBound.yMin, Is.GreaterThanOrEqualTo(image.worldBound.yMax));
                Assert.That(controls.worldBound.xMax, Is.LessThanOrEqualTo(window.rootVisualElement.worldBound.xMax));
            }
            finally { window.Close(); Selection.objects = beforeSelection; }
        }

        [Test] public void ProjectFbxProducesInspectableSampleReport()
        {
            var source = ImportedSource(); var geometry = MeshGeometry.Read(source);
            var watch = System.Diagnostics.Stopwatch.StartNew(); var reduction = Reduce(geometry); watch.Stop();
            var candidate = Own(geometry.Build(reduction.Submeshes));
            string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../.."));
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "sample-report.json"),
                "{\"model\":\"" + source.name + "\",\"originalTriangles\":" + geometry.Triangles + ",\"recommendedTriangles\":" + reduction.Triangles + ",\"originalVertices\":" + geometry.VertexCount + ",\"resultVertices\":" + candidate.vertexCount + ",\"nativeMilliseconds\":" + watch.ElapsedMilliseconds + "}");
            using (var preview = new MeshThumbnails())
            {
                var left = Own(preview.Render(source, geometry.Bounds, 400, 300));
                var right = Own(preview.Render(candidate, geometry.Bounds, 400, 300));
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "sample-original.png"), left.EncodeToPNG());
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "sample-recommended.png"), right.EncodeToPNG());
            }
            Assert.That(candidate.vertexCount, Is.LessThanOrEqualTo(geometry.VertexCount));
        }
    }
}
