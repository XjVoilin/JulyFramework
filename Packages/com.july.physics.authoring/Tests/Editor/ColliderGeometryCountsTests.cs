using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace July.Physics.Authoring
{
    public sealed class ColliderGeometryCountsTests
    {
        [Test] public void CountsEveryTriangleSubmeshWithoutReadingBuffers()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward }, subMeshCount = 3 };
            try
            {
                mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
                mesh.SetIndices(new[] { 0, 2, 3 }, MeshTopology.Triangles, 1);
                mesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 2);
                mesh.UploadMeshData(true);
                var counts = ColliderGeometryCounts.FromMesh(mesh);
                Assert.That(counts.Vertices, Is.EqualTo(4)); Assert.That(counts.Triangles, Is.EqualTo(2));
                Assert.That(counts.MissingMeshes, Is.Zero);
                Assert.That(ColliderGeometryCounts.FromMesh(null).MissingMeshes, Is.EqualTo(1));
                var large = new ColliderGeometryCounts(int.MaxValue, int.MaxValue) + new ColliderGeometryCounts(4, 4);
                Assert.That(large.Vertices, Is.EqualTo((long)int.MaxValue + 4));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        static PrimitiveColliderFitter.Shape Tetra() => new PrimitiveColliderFitter.Shape
        {
            Type = PrimitiveColliderFitter.Kind.Convex,
            ConvexVertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward },
            ConvexTriangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 }
        };
        static ColliderGeneration.Preview Candidate() => new ColliderGeneration.Preview
        {
            Fit = new PrimitiveColliderFitter.Result { Shapes = new[] { Tetra(), new PrimitiveColliderFitter.Shape { Type = PrimitiveColliderFitter.Kind.Box, Size = Vector3.one } } },
            Settings = ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Structure), FitMatrix = Matrix4x4.identity
        };

        [Test] public void CandidateCountsRawConvexesAndExcludesNativePreviewTessellation()
        {
            var counts = ColliderGeometryCounts.Candidate(Candidate());
            Assert.That(counts.Vertices, Is.EqualTo(4)); Assert.That(counts.Triangles, Is.EqualTo(4));
        }

        string folder, id;
        byte[] originalSettings;
        Mesh mesh;
        [SetUp] public void Setup()
        {
            id = "geometry-counts-" + Guid.NewGuid().ToString("N"); folder = "Assets/__" + id;
            originalSettings = File.Exists(ColliderAuthoringSettings.FileName) ? File.ReadAllBytes(ColliderAuthoringSettings.FileName) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(ColliderAuthoringSettings.FileName));
            Directory.CreateDirectory(folder); AssetDatabase.Refresh(); var shape = Tetra();
            mesh = new Mesh { vertices = shape.ConvexVertices, triangles = shape.ConvexTriangles }; mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, folder + "/Tetra.asset");
        }
        [TearDown] public void Cleanup()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<TuningTestWindow>()) window.Close();
            AssetDatabase.DeleteAsset(folder);
            string review = "ProjectSettings/July/ColliderAuthoring/" + id + ".reviews.json";
            if (File.Exists(review)) File.Delete(review);
            if (originalSettings == null) File.Delete(ColliderAuthoringSettings.FileName);
            else File.WriteAllBytes(ColliderAuthoringSettings.FileName, originalSettings);
        }
        string Prefab(string name)
        {
            var root = new GameObject(name);
            for (int i = 0; i < 2; i++)
            {
                var model = new GameObject("Model" + i); model.transform.SetParent(root.transform, false);
                model.AddComponent<MeshFilter>().sharedMesh = mesh; model.AddComponent<MeshRenderer>();
            }
            var whole = new GameObject("Whole"); whole.transform.SetParent(root.transform, false); whole.AddComponent<BoxCollider>();
            var fine = new GameObject("Fine"); fine.transform.SetParent(root.transform, false);
            for (int i = 0; i < 2; i++)
            {
                var shape = new GameObject("Mesh" + i); shape.transform.SetParent(fine.transform, false);
                var collider = shape.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.convex = true;
            }
            var backup = fine.AddComponent<MeshCollider>(); backup.sharedMesh = mesh; backup.enabled = false;
            string path = folder + "/" + name + ".prefab"; PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root); return path;
        }
        [Test] public void CurrentAndModelCountSharedMeshInstancesButIgnoreDisabledBackups()
        {
            string path = Prefab("a"); var adapter = new TuningTestAdapter(id, folder);
            var inspection = ColliderInspection.Read(path, adapter, adapter.CreateProfile());
            Assert.That(inspection.Ready, Is.True, inspection.Error);
            Assert.That(inspection.ModelGeometry.Vertices, Is.EqualTo(8)); Assert.That(inspection.ModelGeometry.Triangles, Is.EqualTo(8));
            Assert.That(inspection.Current[0].Geometry.Vertices, Is.Zero);
            Assert.That(inspection.Current[1].Geometry.Vertices, Is.EqualTo(8)); Assert.That(inspection.Current[1].Geometry.Triangles, Is.EqualTo(8));
        }
        [UnityTest] public IEnumerator SelectionTotalsFollowVisibleSelectionScopeAndMissingCandidates()
        {
            string first = Prefab("a"), second = Prefab("b");
            TuningTestWindow.Adapter = new TuningTestAdapter(id, folder, true);
            var window = ScriptableObject.CreateInstance<TuningTestWindow>(); window.position = new Rect(0, 0, 1100, 800); window.Show();
            try
            {
                window.Workspace.Read(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(first), AssetDatabase.LoadAssetAtPath<GameObject>(second) });
                var rows = window.Workspace.Rows; rows[0].Selected = rows[1].Selected = true;
                rows[0].Candidates[1].Preview = Candidate(); window.Refresh();
                for (int i = 0; i < 8; i++) yield return null;
                var root = window.rootVisualElement; var totals = root.Q<Label>("collider-geometry-totals");
                Assert.That(totals.text, Does.Contain("原模型合计：顶点 16 · 三角面 16"));
                Assert.That(totals.text, Does.Contain("当前：顶点 16 · 三角面 16"));
                Assert.That(totals.text, Does.Contain("候选（1/4组）：顶点 4 · 三角面 4"));
                root.Q<PopupField<string>>("collider-scope").index = 1; yield return null;
                Assert.That(totals.text, Does.Contain("当前：顶点 0 · 三角面 0")); Assert.That(totals.text, Does.Contain("未生成"));
                root.Q<PopupField<string>>("collider-scope").index = 2;
                root.Q<UnityEditor.UIElements.ToolbarSearchField>("collider-search").value = "/a.prefab";
                window.Refresh(); for (int i = 0; i < 8; i++) yield return null;
                Assert.That(totals.text, Does.Contain("原模型合计：顶点 8 · 三角面 8"));
                Assert.That(totals.text, Does.Contain("当前：顶点 8 · 三角面 8"));
                Assert.That(totals.text, Does.Contain("候选：顶点 4 · 三角面 4"));
                Assert.That(root.Query<Label>(name: "collider-geometry-0").ToList().Any(l => l.text == "顶点 8\n三角面 8"));
                Assert.That(root.Query<Label>(name: "collider-geometry-4").ToList().Any(l => l.text == "顶点 4 · 三角面 4"));
            }
            finally { window.Close(); }
        }

        [UnityTest] public IEnumerator GeometryMenusSortTheirOwnColumnInBothDirectionsWithMissingLast()
        {
            var paths = new[] { Prefab("a"), Prefab("b"), Prefab("c") };
            TuningTestWindow.Adapter = new TuningTestAdapter(id, folder, true);
            var window = ScriptableObject.CreateInstance<TuningTestWindow>(); window.Show();
            try
            {
                window.Workspace.Read(paths.Select(p => (Object)AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToArray());
                var a = window.Workspace.Rows.Single(r => r.Name == "a"); var b = window.Workspace.Rows.Single(r => r.Name == "b"); var c = window.Workspace.Rows.Single(r => r.Name == "c");
                // Cached metadata avoids allocating multi-billion-vertex test meshes.
                a.Inspection.ModelGeometry = new ColliderGeometryCounts((long)int.MaxValue + 10, 2);
                b.Inspection.ModelGeometry = new ColliderGeometryCounts(4, 8);
                a.Inspection.Current[1].Geometry = new ColliderGeometryCounts(100, 1);
                b.Inspection.Current[1].Geometry = new ColliderGeometryCounts(1, 100);
                c.Inspection.Error = "fixture unreadable";
                a.Candidates[1].Preview = Candidate(); b.Candidates[1].Preview = Candidate();
                b.Candidates[1].Preview.Fit.Shapes = new[] { Tetra(), Tetra() };
                window.Refresh(); for (int i = 0; i < 5; i++) yield return null;
                void Sort(string menu, string action) => window.rootVisualElement.Q<ToolbarMenu>(menu).menu.MenuItems().OfType<DropdownMenuAction>().Single(item => item.name == action).Execute();
                void Order(params string[] expected) => CollectionAssert.AreEqual(expected, window.Workspace.Rows.Select(r => r.Name).ToArray());
                Sort("collider-model-column", "顶点数"); Order("a", "b", "c");
                Assert.That(window.rootVisualElement.Q<ToolbarMenu>("collider-model-column").text, Does.Contain("↓"));
                Sort("collider-model-column", "顶点数"); Order("b", "a", "c");
                Sort("collider-model-column", "三角面数"); Order("b", "a", "c");
                Sort("collider-column-2", "碰撞网格顶点数"); Order("a", "b", "c");
                Sort("collider-column-2", "碰撞网格三角面数"); Order("b", "a", "c");
                Sort("collider-column-2", "碰撞网格三角面数"); Order("a", "b", "c");
                Sort("collider-column-3", "碰撞网格顶点数"); Order("b", "a", "c");
                Sort("collider-column-3", "碰撞网格顶点数"); Order("a", "b", "c");
                Sort("collider-column-3", "碰撞网格三角面数"); Order("b", "a", "c");
                a.Candidates[1].Preview = null; window.Refresh();
                Sort("collider-column-3", "碰撞网格三角面数"); Order("b", "a", "c");
                Sort("collider-column-0", "碰撞网格三角面数"); Order("a", "b", "c");
                Sort("collider-model-column", "顶点数"); Order("a", "b", "c");
                b.Inspection.ModelGeometry = new ColliderGeometryCounts((long)int.MaxValue + 20, 8);
                window.Refresh(); Order("b", "a", "c");
                window.Workspace.RefreshProjectChanges();
                Assert.That(window.Workspace.Rows.Single(r => r.Name == "a"), Is.SameAs(a), "sorting must not make a routine resource update rebuild live rows");
                Assert.That(window.Workspace.Rows.Single(r => r.Name == "b"), Is.SameAs(b));
                Order("a", "b", "c");
            }
            finally { window.Close(); }
        }
    }
}
