using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring.Tests
{
    public sealed class MeshBatchColliderTests
    {
        const string Folder = "Assets/__MeshBatchColliderTests";
        readonly List<MeshRow> rows = new List<MeshRow>();
        readonly List<string> caches = new List<string>();
        MeshAuthoringWindow window;

        [SetUp] public void SetUp()
        {
            MeshAssetEdits.EnsureFolder(Folder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Active.unity");
            window = EditorWindow.GetWindow<MeshAuthoringWindow>();
        }

        [TearDown] public void TearDown()
        {
            if (window) window.Close(); rows.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (string path in caches) if (File.Exists(path)) File.Delete(path);
            caches.Clear(); AssetDatabase.DeleteAsset(Folder);
        }

        MeshRow Row(string name, bool reduce = true)
        {
            const int size = 8;
            var vertices = new List<Vector3>(); var indices = new List<int>();
            for (int y = 0; y <= size; y++) for (int x = 0; x <= size; x++) vertices.Add(new Vector3(x, 0, y));
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            { int i = y * (size + 1) + x; indices.AddRange(new[] { i, i + size + 1, i + 1, i + 1, i + size + 1, i + size + 2 }); }
            var source = new UMesh { name = name }; source.SetVertices(vertices); source.SetTriangles(indices, 0);
            source.RecalculateNormals(); source.RecalculateBounds(); AssetDatabase.CreateAsset(source, Folder + "/" + name + ".asset");
            var row = new MeshRow { Source = source }; row.Load(); rows.Add(row); window.Workspace.Rows.Add(row);
            caches.Add(MeshBaselineCache.PathFor(MeshAssetEdits.Identity(source)));
            if (reduce)
            {
                row.Ratio = 50; row.Automatic = false;
                row.SetCandidate(MeshReduction.Calculate(row.Geometry, 50, false, 0, CancellationToken.None));
                Assert.That(row.HasChanges, Is.True);
            }
            return row;
        }

        static GameObject Collider(UMesh mesh)
        { var go = new GameObject("Collider"); go.AddComponent<MeshCollider>().sharedMesh = mesh; return go; }

        void Prefab(string name, UMesh mesh, bool collider = false)
        {
            var go = collider ? Collider(mesh) : new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            PrefabUtility.SaveAsPrefabAsset(go, Folder + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }

        string Message => window.rootVisualElement.Query<Label>().ToList()
            .Select(label => label.text).Single(text => text != null && text.StartsWith("完成 "));

        [Test] public void BatchKeepsPerItemColliderFailureAndZeroChangeSkip()
        {
            var first = Row("First"); var blocked = Row("Blocked"); var last = Row("Last"); var zero = Row("Zero", false);
            Collider(blocked.Source); Prefab("Shared", first.Source);
            string blockedBefore = blocked.ObservedHash, zeroBefore = zero.ObservedHash;
            var scanned = new List<string>();
            LogAssert.Expect(LogType.Error, new Regex("July Mesh：Blocked 操作失败：.*MeshCollider"));
            window.Batch(false, (title, path, progress) => { if (title == "检查 Mesh 碰撞引用") scanned.Add(path); return false; });
            Assert.That(first.HasChanges, Is.False); Assert.That(last.HasChanges, Is.False);
            Assert.That(blocked.Error, Does.Contain("MeshCollider"));
            Assert.That(MeshGeometry.Read(blocked.Source).Fingerprint(), Is.EqualTo(blockedBefore));
            Assert.That(MeshGeometry.Read(zero.Source).Fingerprint(), Is.EqualTo(zeroBefore));
            Assert.That(Message, Is.EqualTo("完成 2，跳过 1，失败 1，未处理 0。"));
            Assert.That(scanned.Count, Is.GreaterThan(1));
            Assert.That(scanned.Distinct().Count(), Is.EqualTo(scanned.Count), "本批每个引用资源只检查一次。");
        }

        [Test] public void ScanChecksPrefabAndUnloadedSceneWithoutSavingOrClosingLoadedScenes()
        {
            var sceneRow = Row("SceneMesh"); var prefabRow = Row("PrefabMesh"); var allowed = Row("Allowed");
            Prefab("Persistent", prefabRow.Source, true);
            var active = SceneManager.GetActiveScene();
            var loaded = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(loaded, Folder + "/Loaded.unity");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var go = Collider(sceneRow.Source); SceneManager.MoveGameObjectToScene(go, scene);
            string scenePath = Folder + "/Unloaded.unity";
            EditorSceneManager.SaveScene(scene, scenePath); EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(active);
            byte[] before = File.ReadAllBytes(scenePath); int sceneCount = SceneManager.sceneCount;
            var usage = MeshColliderUsage.Scan(rows.Select(row => row.Source));
            Assert.Throws<InvalidOperationException>(() => usage.Check(sceneRow.Source));
            Assert.Throws<InvalidOperationException>(() => usage.Check(prefabRow.Source));
            Assert.DoesNotThrow(() => usage.Check(allowed.Source));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCount));
            Assert.That(loaded.isLoaded, Is.True);
            Assert.That(SceneManager.GetSceneByPath(scenePath).isLoaded, Is.False);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(scenePath));
        }

        [Test] public void CancellingDuringScanLeavesAllItemsUnprocessed()
        {
            Row("First"); Row("Second"); Prefab("Reference", rows[0].Source);
            window.Batch(false, (title, path, progress) => title == "检查 Mesh 碰撞引用" && progress > 0);
            Assert.That(rows.All(row => row.HasChanges && row.Error == null), Is.True);
            Assert.That(rows.All(row => MeshGeometry.Read(row.Source).Fingerprint() == row.ObservedHash), Is.True);
            Assert.That(Message, Is.EqualTo("完成 0，跳过 0，失败 0，未处理 2。"));
        }

        [Test] public void CancellingAfterOneWriteKeepsCompletedItem()
        {
            var first = Row("First"); var second = Row("Second");
            window.Batch(false, (title, path, progress) => title == "应用 Mesh" && progress > 0);
            Assert.That(first.HasChanges, Is.False); Assert.That(first.HasApplied, Is.True);
            Assert.That(second.HasChanges, Is.True); Assert.That(second.HasApplied, Is.False);
            Assert.That(MeshGeometry.Read(second.Source).Fingerprint(), Is.EqualTo(second.ObservedHash));
            Assert.That(Message, Is.EqualTo("完成 1，跳过 0，失败 0，未处理 1。"));
        }

        [Test] public void BatchRestoreBuildsFreshColliderSnapshot()
        {
            var first = Row("First"); var blocked = Row("Blocked");
            string baseline = first.BaselineHash;
            window.Batch(false, (title, path, progress) => false);
            string applied = blocked.ObservedHash;
            Collider(blocked.Source);
            LogAssert.Expect(LogType.Error, new Regex("July Mesh：Blocked 操作失败：.*MeshCollider"));
            window.Batch(true, (title, path, progress) => false);
            Assert.That(first.ObservedHash, Is.EqualTo(baseline)); Assert.That(first.CanRestore, Is.False);
            Assert.That(blocked.ObservedHash, Is.EqualTo(applied)); Assert.That(blocked.CanRestore, Is.True);
            Assert.That(Message, Is.EqualTo("完成 1，跳过 0，失败 1，未处理 0。"));
        }

        [Test] public void ExternalEditStillFailsOnlyItsItemAfterSharedScan()
        {
            Row("First"); var edited = Row("Edited"); var last = Row("Last");
            string external = null;
            LogAssert.Expect(LogType.Error, new Regex("July Mesh：Edited 操作失败：来源已改变"));
            window.Batch(false, (title, path, progress) =>
            {
                if (title == "应用 Mesh" && path == "Edited")
                {
                    var vertices = edited.Source.vertices; vertices[0] += Vector3.up; edited.Source.vertices = vertices;
                    external = MeshGeometry.Read(edited.Source).Fingerprint();
                }
                return false;
            });
            Assert.That(MeshGeometry.Read(edited.Source).Fingerprint(), Is.EqualTo(external));
            Assert.That(last.HasChanges, Is.False);
            Assert.That(Message, Is.EqualTo("完成 2，跳过 0，失败 1，未处理 0。"));
        }
    }
}
