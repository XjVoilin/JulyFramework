using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring.Tests
{
    public sealed class MeshResourceLifecycleTests
    {
        const string Folder = "Assets/__MeshResourceLifecycle";
        readonly List<MeshRow> rows = new List<MeshRow>();
        readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
        readonly List<string> caches = new List<string>();
        [SetUp] public void SetUp() { MeshAssetEdits.EnsureFolder(Folder); }
        [TearDown] public void TearDown()
        {
            foreach (var row in rows) row.Dispose(); rows.Clear();
            foreach (var value in temporary) if (value) UnityEngine.Object.DestroyImmediate(value); temporary.Clear();
            foreach (string file in caches) if (File.Exists(file)) File.Delete(file); caches.Clear();
            AssetDatabase.DeleteAsset(Folder);
        }
        UMesh Grid()
        {
            var positions = new List<Vector3>(); var indices = new List<int>(); const int size = 12;
            for (int y = 0; y <= size; y++) for (int x = 0; x <= size; x++) positions.Add(new Vector3(x, 0, y));
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            { int i = y * (size + 1) + x; indices.AddRange(new[] { i, i + size + 1, i + 1, i + 1, i + size + 1, i + size + 2 }); }
            var mesh = new UMesh(); mesh.SetVertices(positions); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, Folder + "/Grid.asset"); return mesh;
        }
        MeshRow Row(UMesh source)
        {
            var row = new MeshRow { Source = source }; row.Load(); rows.Add(row);
            caches.Add(MeshBaselineCache.PathFor(MeshAssetEdits.Identity(source))); return row;
        }
        static IEnumerator Calculate(MeshRow row, float ratio)
        {
            using (var workspace = new MeshWorkspace())
            {
                workspace.Rows.Add(row); workspace.Request(row, ratio, false);
                for (int i = 0; workspace.Working && i < 5000; i++) { workspace.Tick(); yield return null; }
                workspace.Rows.Clear(); Assert.That(row.Error, Is.Null);
            }
        }

        [UnityTest] public IEnumerator StandaloneApplyRescanAndWindowReopenKeepTheAppliedResult()
        {
            var source = Grid(); string identity = MeshAssetEdits.Identity(source);
            var row = Row(source); yield return Calculate(row, 50);
            string expected = row.CandidateHash; int triangles = row.CandidateTriangles;
            MeshAssets.Apply(row, 0);
            Assert.That(row.HasChanges, Is.False); Assert.That(row.CurrentTriangles, Is.EqualTo(triangles));
            Assert.That(MeshAssetEdits.Identity(source), Is.EqualTo(identity));
            var selection = Selection.objects;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    var window = EditorWindow.GetWindow<MeshAuthoringWindow>();
                    try
                    {
                        Selection.objects = new UnityEngine.Object[] { source }; window.ReadSelection();
                        for (int i = 0; window.Workspace.Working && i < 5000; i++) { window.Workspace.Tick(); yield return null; }
                        var scanned = window.Workspace.Rows.Single();
                        Assert.That(scanned.Ratio, Is.EqualTo(50)); Assert.That(scanned.Automatic, Is.False);
                        Assert.That(scanned.CurrentTriangles, Is.EqualTo(triangles)); Assert.That(scanned.CandidateHash, Is.EqualTo(expected));
                        Assert.That(scanned.HasChanges, Is.False);
                        Assert.That(window.rootVisualElement.Q<UnityEngine.UIElements.Label>("mesh-selection-summary").text, Does.Contain("预计减少 0 面"));
                    }
                    finally { window.Close(); }
                }
            }
            finally { Selection.objects = selection; }
            MeshAssets.Restore(row); Assert.That(row.CurrentTriangles, Is.EqualTo(288)); Assert.That(row.HasApplied, Is.False);
            Assert.That(AssetDatabase.GetSubFolders(Folder), Is.Empty, "减面流程不在 Assets 下创建备份或输出目录。");
        }

        [UnityTest] public IEnumerator MissingStandaloneCacheUsesCurrentMeshAsTheNewBaseline()
        {
            var source = Grid(); var row = Row(source); yield return Calculate(row, 50); MeshAssets.Apply(row, 0);
            int reduced = row.CurrentTriangles; string current = row.ObservedHash;
            File.Delete(MeshBaselineCache.PathFor(MeshAssetEdits.Identity(source)));
            var rescanned = Row(source);
            Assert.That(rescanned.OriginalTriangles, Is.EqualTo(reduced)); Assert.That(rescanned.HasApplied, Is.False);
            Assert.That(rescanned.Ratio, Is.EqualTo(100)); Assert.That(rescanned.BaselineHash, Is.EqualTo(current));
            MeshAssets.Restore(rescanned); Assert.That(MeshGeometry.Read(source).Fingerprint(), Is.EqualTo(current));
        }

        [UnityTest] public IEnumerator FidelityOnlyChangesArePersistedEvenWhenGeometryIsIdentical()
        {
            var row = Row(Grid());
            using (var workspace = new MeshWorkspace())
            {
                workspace.Rows.Add(row); workspace.Request(row, 100, true);
                for (int i = 0; workspace.Working && i < 5000; i++) { workspace.Tick(); yield return null; }
                MeshAssets.Apply(row, 0); string current = row.ObservedHash;
                workspace.Fidelity = 2; workspace.Request(row, 100, true);
                for (int i = 0; workspace.Working && i < 5000; i++) { workspace.Tick(); yield return null; }
                Assert.That(row.CandidateHash, Is.EqualTo(current), "该平面样本用于覆盖几何不变但设置变化的情况。");
                Assert.That(row.HasChanges, Is.True); MeshAssets.Apply(row, 2);
                Assert.That(row.Cache.Fidelity, Is.EqualTo(2)); Assert.That(row.HasChanges, Is.False);
                workspace.Rows.Clear();
            }
        }

        [UnityTest] public IEnumerator ExternalEditsBetweenPreviewAndApplyAreRejected()
        {
            var source = Grid(); var row = Row(source); yield return Calculate(row, 50);
            var points = source.vertices; points[0] += Vector3.up; source.vertices = points;
            string external = MeshGeometry.Read(source).Fingerprint();
            Assert.Throws<InvalidOperationException>(() => MeshAssets.Apply(row, 0));
            Assert.That(MeshGeometry.Read(source).Fingerprint(), Is.EqualTo(external));
            row.NeedsSourceCheck = true; row.Load();
            Assert.That(row.BaselineHash, Is.EqualTo(external), "重新读取时按照已确认策略，把外部编辑后的当前资源作为新基准。");
        }
    }
}
