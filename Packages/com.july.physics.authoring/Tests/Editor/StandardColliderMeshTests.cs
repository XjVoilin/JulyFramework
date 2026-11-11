using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Shape = July.Physics.Authoring.PrimitiveColliderFitter.Shape;

namespace July.Physics.Authoring
{
    public sealed class StandardColliderMeshTests
    {
        string folder;

        [SetUp] public void SetUp() => folder = "Assets/__JulyStandardMeshTests_" + Guid.NewGuid().ToString("N");
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            AssetDatabase.DeleteAsset(folder);
        }

        static Shape Make(int kind, float factor = 1, float top = 1, float height = 3) =>
            SixPrimitiveMeshes.Convert(new SixPrimitiveMath.Primitive
            {
                Kind = (SixPrimitiveMath.PrimitiveKind)kind, Radius = 2 * factor, Height = height * factor,
                Bottom = 2 * factor, Top = top * factor, HalfSize = new Vector3(.75f, 1.5f, 1) * factor,
                Center = new Vector3(.4f, -.2f, .1f), Rotation = Quaternion.Euler(17, 31, 9)
            });

        [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void DifferentDimensionsShareOnlyTheStandardGeometry(int kind)
        {
            var first = FineColliderMeshAssets.GetOrCreate(Make(kind), folder, out var a);
            var other = Make(kind, 4, height: 7);
            if (kind == 5) other = SixPrimitiveMeshes.Convert(new SixPrimitiveMath.Primitive {
                Kind = SixPrimitiveMath.PrimitiveKind.Prism, HalfSize = new Vector3(7, .3f, 1),
                Bottom = 8, Top = 4, Rotation = Quaternion.identity });
            var second = FineColliderMeshAssets.GetOrCreate(other, folder, out var b);
            Assert.That(second, Is.SameAs(first)); Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(Directory.GetFiles(folder, "*.asset").Length, Is.EqualTo(1));
            Assert.That(first.name, Does.StartWith(kind == 3 ? "Cylinder_S32_v1" : kind == 4 ? "Frustum_S32_v1_" : "TrapezoidPrism_v1_"));
        }

        [TestCase(4)] [TestCase(5)]
        public void DifferentProfilesAndOppositeTapersRemainSeparate(int kind)
        {
            var first = FineColliderMeshAssets.GetOrCreate(Make(kind), folder, out _);
            var different = FineColliderMeshAssets.GetOrCreate(Make(kind, top: .5f), folder, out _);
            var reversed = Make(kind); reversed.Bottom = 1; reversed.Top = 2;
            var opposite = FineColliderMeshAssets.GetOrCreate(reversed, folder, out _);
            Assert.That(different, Is.Not.SameAs(first)); Assert.That(opposite, Is.Not.SameAs(first));
        }

        [Test]
        public void ExistingModifiedMeshIsRejectedWithoutOverwritingIt()
        {
            var shape = Make(3); var mesh = FineColliderMeshAssets.GetOrCreate(shape, folder, out _);
            var vertices = mesh.vertices; vertices[0] += Vector3.right; mesh.vertices = vertices;
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
            string path = AssetDatabase.GetAssetPath(mesh); var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => FineColliderMeshAssets.GetOrCreate(shape, folder, out _));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        }

        [Test]
        public void OccupiedAssetPathIsRejectedWithoutReplacement()
        {
            FineColliderMeshAssets.EnsureFolder(folder);
            string path = folder + "/Cylinder_S32_v1.asset";
            AssetDatabase.CreateAsset(new PhysicMaterial("occupied"), path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            Assert.Throws<InvalidDataException>(() => FineColliderMeshAssets.GetOrCreate(Make(3), folder, out _));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(path), Is.TypeOf<PhysicMaterial>());
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
        }

        [TestCase(3, 1f)] [TestCase(4, 1f)] [TestCase(4, 0f)]
        [TestCase(5, 1f)] [TestCase(5, 0f)]
        [TestCase(3, 1f, .01f, 50f)] [TestCase(4, .001f, 1f, .01f)]
        public void ScaledStandardPreservesGeometryAndCollisionSurface(int kind, float top, float factor = 1, float height = 3)
        {
            var shape = Make(kind, factor, top, height);
            var saved = FineColliderMeshAssets.GetOrCreate(shape, folder, out var scale);
            var node = new GameObject("standard");
            try
            {
                node.transform.SetPositionAndRotation(shape.Center, shape.Rotation);
                node.transform.localScale = scale;
                var collider = node.AddComponent<MeshCollider>(); collider.convex = true; collider.sharedMesh = saved;
                var pose = Matrix4x4.TRS(shape.Center, shape.Rotation, Vector3.one);
                var inverse = pose.inverse;
                var vertices = saved.vertices;
                float tolerance = Mathf.Max(1e-5f, shape.Size.magnitude * 1e-5f);
                for (int i = 0; i < vertices.Length; i++)
                    Assert.That(Vector3.Distance(node.transform.TransformPoint(vertices[i]), pose.MultiplyPoint3x4(shape.ConvexVertices[i])), Is.LessThan(tolerance));
                CollectionAssert.AreEqual(shape.ConvexTriangles, saved.triangles);
                UnityEngine.Physics.SyncTransforms();
                float distance = shape.Size.magnitude * 3 + 1;
                foreach (var local in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back, new Vector3(1, 1, 1).normalized })
                {
                    var direction = shape.Rotation * local;
                    var ray = new Ray(shape.Center + direction * distance, -direction);
                    var localRay = new Ray(inverse.MultiplyPoint3x4(ray.origin), inverse.MultiplyVector(ray.direction));
                    bool expected = ColliderSurfaceReference.Raycast(localRay, shape.ConvexVertices, shape.ConvexTriangles, distance * 2, out _);
                    bool actual = collider.Raycast(ray, out var hit, distance * 2);
                    Assert.That(actual, Is.EqualTo(expected), "source ray hit/miss " + local);
                    if (actual)
                    {
                        var point = inverse.MultiplyPoint3x4(hit.point);
                        Assert.That(Vector3.Distance(point, ColliderSurfaceReference.ClosestSurface(point, shape.ConvexVertices, shape.ConvexTriangles)), Is.LessThan(tolerance));
                    }
                    var closest = inverse.MultiplyPoint3x4(collider.ClosestPoint(ray.origin));
                    Assert.That(Vector3.Distance(closest, ColliderSurfaceReference.ClosestSurface(closest, shape.ConvexVertices, shape.ConvexTriangles)), Is.LessThan(tolerance));
                }
            }
            finally { Object.DestroyImmediate(node); }
        }

        [Test]
        public void SavedPrefabRetainsSharedReferenceScaleAndGeometry()
        {
            var root = new GameObject("shared");
            try
            {
                var shape = Make(4); var mesh = FineColliderMeshAssets.GetOrCreate(shape, folder, out var scale);
                string meshPath = AssetDatabase.GetAssetPath(mesh), guid = AssetDatabase.AssetPathToGUID(meshPath);
                root.transform.localScale = scale;
                var collider = root.AddComponent<MeshCollider>(); collider.convex = true; collider.sharedMesh = mesh;
                PrefabUtility.SaveAsPrefabAsset(root, folder + "/item.prefab");
                AssetDatabase.ImportAsset(meshPath, ImportAssetOptions.ForceUpdate);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/item.prefab");
                Assert.That(prefab.transform.localScale, Is.EqualTo(scale));
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab.GetComponent<MeshCollider>().sharedMesh)), Is.EqualTo(guid));
                FineColliderMeshAssets.GetOrCreate(shape, folder, out _);
                Assert.That(Directory.GetFiles(folder, "*.asset").Length, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator SharedStandardMeshesSupportFallingBodiesAndStacking()
        {
            yield return new EnterPlayMode();
            foreach (int kind in new[] { 3, 4, 5 })
            {
                var scene = SceneManager.CreateScene("standard-contacts-" + kind, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                AsyncOperation unloading;
                try
                {
                    var floor = new GameObject("floor"); SceneManager.MoveGameObjectToScene(floor, scene);
                    floor.transform.position = new Vector3(0, -.5f, 0);
                    floor.AddComponent<BoxCollider>().size = new Vector3(30, 1, 30);
                    var shape = Make(kind);
                    var mesh = FineColliderMeshAssets.GetOrCreate(shape, folder, out var scale);
                    var bodies = new Rigidbody[2];
                    for (int i = 0; i < bodies.Length; i++)
                    {
                        var item = new GameObject("body-" + i); SceneManager.MoveGameObjectToScene(item, scene);
                        item.transform.position = new Vector3(0, 4 + i * (shape.Size.y + 1), 0);
                        var child = new GameObject("shape"); child.transform.SetParent(item.transform, false);
                        child.transform.localScale = scale;
                        var collider = child.AddComponent<MeshCollider>(); collider.convex = true; collider.sharedMesh = mesh;
                        bodies[i] = item.AddComponent<Rigidbody>(); bodies[i].mass = .137f;
                    }
                    var physics = scene.GetPhysicsScene();
                    for (int step = 0; step < 600; step++) physics.Simulate(.01f);
                    Assert.That(bodies[0].position.y, Is.EqualTo(shape.Size.y * .5f).Within(.05f), "floor contact " + kind);
                    Assert.That(bodies[1].position.y - bodies[0].position.y, Is.EqualTo(shape.Size.y).Within(.08f), "stack contact " + kind);
                    Assert.That(bodies.All(b => b.velocity.magnitude < .1f), Is.True, "settled " + kind);
                }
                finally { unloading = SceneManager.UnloadSceneAsync(scene); }
                yield return unloading;
            }
            yield return new ExitPlayMode();
        }
    }
}
