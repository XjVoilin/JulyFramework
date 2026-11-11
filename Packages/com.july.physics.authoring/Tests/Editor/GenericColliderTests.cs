using System;
using System.Collections;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.Physics.Authoring
{
    public sealed class GenericColliderTests
    {
        static ColliderGeometry.Geometry Geometry(Mesh mesh) => new ColliderGeometry.Geometry{Vertices = mesh.vertices, Indices = mesh.triangles};
        static Mesh ShapeMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var m = UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.DestroyImmediate(go);
            return m;
        }

        static Mesh U()
        {
            var cube = ShapeMesh(PrimitiveType.Cube);
            var mesh = new Mesh();
            mesh.CombineMeshes(new[]{new CombineInstance{mesh = cube, transform = Matrix4x4.TRS(new Vector3(-.7f, .3f, 0), Quaternion.identity, new Vector3(.3f, 1.4f, .3f))}, new CombineInstance{mesh = cube, transform = Matrix4x4.TRS(new Vector3(.7f, .3f, 0), Quaternion.identity, new Vector3(.3f, 1.4f, .3f))}, new CombineInstance{mesh = cube, transform = Matrix4x4.TRS(new Vector3(0, -.3f, 0), Quaternion.identity, new Vector3(1.7f, .3f, .3f))}});
            UnityEngine.Object.DestroyImmediate(cube);
            return mesh;
        }

        [Test]
        public void EnvelopeFitsSimpleSolid()
        {
            var mesh = ShapeMesh(PrimitiveType.Cube);
            try
            {
                var fit = PrimitiveColliderFitter.Fit(Geometry(mesh), new ColliderGenerationSettings{Goal = ColliderGeometryGoal.Envelope, TargetCount = 1}, null, default);
                Assert.That(fit.Shapes.Length, Is.EqualTo(1));
                Assert.That(PrimitiveColliderFitter.Contains(fit.Shapes[0], Vector3.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(3,0f)] [TestCase(3,8f)] [TestCase(4,8f)] [TestCase(5,8f)]
        public void EnvelopeFitsCurvedAndTaperedSourcesUsingOnlyNativeShapes(int kind,float error)
        {
            var source=SixPrimitiveMeshes.Convert(new SixPrimitiveMath.Primitive{Kind=(SixPrimitiveMath.PrimitiveKind)kind,
                Radius=.35f,Height=1,Bottom=.45f,Top=.2f,HalfSize=new Vector3(.4f,.5f,.25f),Rotation=Quaternion.identity});
            var geometry=new ColliderGeometry.Geometry{Vertices=source.ConvexVertices,Indices=source.ConvexTriangles};
            var settings=ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Envelope);settings.SilhouetteErrorPercent=error;
            var fit=PrimitiveColliderFitter.Fit(geometry,settings,null,default);
            Assert.That(fit.Shapes.Length,Is.InRange(1,3));
            Assert.That(fit.Shapes.All(s=>s.Type==PrimitiveColliderFitter.Kind.Box||s.Type==PrimitiveColliderFitter.Kind.Sphere||s.Type==PrimitiveColliderFitter.Kind.Capsule));
            Assert.That(fit.Shapes.All(s=>s.ConvexVertices==null&&s.ConvexTriangles==null));
        }

        [UnityTest] public IEnumerator EnvelopePublicGenerationAppliesOnlyNativeColliders()
        {
            var source=SixPrimitiveMeshes.Convert(new SixPrimitiveMath.Primitive{Kind=SixPrimitiveMath.PrimitiveKind.Cylinder,
                Radius=.35f,Height=1,Rotation=Quaternion.identity});
            var mesh=new Mesh{vertices=source.ConvexVertices,triangles=source.ConvexTriangles};
            var root=new GameObject("native-envelope");var output=new GameObject("Output");output.transform.SetParent(root.transform,false);
            try
            {
                var settings=ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Envelope);
                var request=ColliderGeneration.CreateRequest(output.transform,settings);request.Sources.Add(new ColliderMeshSource(mesh,root.transform));
                var task=ColliderGeneration.PreviewAsync(request);while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception;
                Assert.That(task.Result.NativeCount,Is.EqualTo(task.Result.Count));
                var loaded=ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(task.Result));
                var applied=ColliderGeneration.ApplyPreview(ColliderGeneration.RetargetPreview(loaded,request));
                Assert.That(applied.Colliders.All(c=>c is BoxCollider||c is SphereCollider||c is CapsuleCollider));
                Assert.That(output.GetComponentsInChildren<MeshCollider>(),Is.Empty);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);}
        }

        [Test]
        public void StructureDoesNotAddResidualRepairShapes()
        {
            var mesh=U();
            try
            {
                var fit=PrimitiveColliderFitter.Fit(Geometry(mesh),new ColliderGenerationSettings{Goal=ColliderGeometryGoal.Structure,TargetCount=1},null,default);
                Assert.That(fit.Shapes.Length,Is.EqualTo(1),"The primitive test baseline does not append residual hull repairs.");
                Assert.That(fit.Shapes.All(s=>s.Type!=PrimitiveColliderFitter.Kind.Convex && Enum.IsDefined(typeof(PrimitiveColliderFitter.Kind),s.Type)),
                    "The primitive baseline must not append the picking proxy's arbitrary convex meshes.");
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }

        [Test]
        public void EnvelopeUsesFewShapesAndDoesNotPreserveTheUGap()
        {
            var mesh = U();
            try
            {
                var settings = ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Envelope);
                Assert.That(settings.TargetCount, Is.EqualTo(3));
                var envelope = PrimitiveColliderFitter.Fit(Geometry(mesh), settings, null, default);
                var fineSettings = ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Structure);
                var structure = PrimitiveColliderFitter.Fit(Geometry(mesh), fineSettings, null, default);
                Assert.That(envelope.Shapes.Length, Is.LessThanOrEqualTo(3));
                Assert.That(envelope.Shapes.Any(s => PrimitiveColliderFitter.Contains(s, new Vector3(0, .4f, 0))), Is.True);
                Assert.That(structure.Shapes.Any(s => PrimitiveColliderFitter.Contains(s, new Vector3(0, .4f, 0))), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void CancellationHasNoResult()
        {
            var mesh = ShapeMesh(PrimitiveType.Cube);
            try
            {
                Assert.Throws<OperationCanceledException>(() => PrimitiveColliderFitter.Fit(Geometry(mesh), new ColliderGenerationSettings(), null, new CancellationToken(true)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [UnityTest]
        public IEnumerator PreviewApplyUndoAndStaleGeometry()
        {
            var root = new GameObject("test-item");
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(root.transform, false);
            var output = new GameObject("output");
            output.transform.SetParent(root.transform, false);
            var old = new GameObject("old-shape");
            old.transform.SetParent(output.transform, false);
            old.AddComponent<BoxCollider>();
            try
            {
                var request = ColliderGeneration.CreateRequest(output.transform, new ColliderGenerationSettings{Goal = ColliderGeometryGoal.Envelope, TargetCount = 1});
                request.Sources.Add(new ColliderMeshSource(visual.GetComponent<MeshFilter>().sharedMesh, visual.transform));
                var task = ColliderGeneration.PreviewAsync(request);
                while (!task.IsCompleted)
                    yield return null;
                if (task.IsFaulted)
                    throw task.Exception;
                Assert.That(old != null);
                var preview = task.Result;
                old.layer = 7;
                Assert.Throws<InvalidOperationException>(() => ColliderGeneration.ApplyPreview(preview));
                old.layer = 0;
                visual.transform.localPosition = Vector3.right;
                Assert.Throws<InvalidOperationException>(() => ColliderGeneration.ApplyPreview(preview));
                visual.transform.localPosition = Vector3.zero;
                var applied = ColliderGeneration.ApplyPreview(preview);
                Assert.That(applied.Colliders.Length, Is.EqualTo(1));
                Assert.That(old == null);
                Undo.PerformUndo();
                Assert.That(output.transform.Find("old-shape") != null);
                Undo.PerformRedo();
                Assert.That(output.GetComponentsInChildren<Collider>().Length, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
