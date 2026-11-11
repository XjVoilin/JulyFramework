using System;
using System.Collections;
using System.Linq;
using System.Threading;
using July.Physics.Authoring.PickingProxy;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Kind = July.Physics.Authoring.PrimitiveColliderFitter.Kind;

namespace July.Physics.Authoring
{
    public sealed class PickingPrimitiveApproximationTests
    {
        static ColliderGenerationSettings Settings(float error = 3, PickingShapeMode mode = PickingShapeMode.PreferPrimitives) =>
            new ColliderGenerationSettings { MaxHullVertices = 24, PickingInwardErrorPercent = error, NativeShapeErrorPercent = error, NativeClickChangePercent = 5, PickingShapes = mode };
        static TriangleMesh Box(Vec3 center, Vec3 half, Quaternion rotation)
        {
            var points = (from x in new[]{-1,1} from y in new[]{-1,1} from z in new[]{-1,1}
                let q = rotation*new Vector3((float)(x*half.X),(float)(y*half.Y),(float)(z*half.Z))
                select center+new Vec3(q.x,q.y,q.z)).ToArray();
            return ConvexHull.Build(points).Mesh;
        }
        static PickingPrimitiveApproximation.Result Convert(ProxyFit fit, ColliderGenerationSettings settings = null) =>
            PickingPrimitiveApproximation.Export(fit,settings ?? Settings(),default);
        static double NativeDistance(PrimitiveColliderFitter.Shape shape, Vec3 point)
        {
            var p = new Vector3((float)point.X,(float)point.Y,(float)point.Z);
            var q = Quaternion.Inverse(shape.Rotation)*(p-shape.Center);
            if (shape.Type == Kind.Box) return Vector3.Max(Vector3.zero,new Vector3(Math.Abs(q.x),Math.Abs(q.y),Math.Abs(q.z))-shape.Size*.5f).magnitude;
            if (shape.Type == Kind.Capsule) q.y -= Mathf.Clamp(q.y,-shape.Height*.5f+shape.Radius,shape.Height*.5f-shape.Radius);
            return Math.Max(0,q.magnitude-shape.Radius);
        }

        [TestCase(0f)] [TestCase(37f)] [TestCase(79f)]
        public void RotatedBoxesBecomeOneNativeBoxWithinTheSpecifiedDeviation(float angle)
        {
            var raw = Box(new Vec3(.03,-.02,.01),new Vec3(.31,.17,.09),Quaternion.Euler(angle,23,11));
            var fit = new ProxyFit(new Vec3(1,2,3),2.5,new[]{raw});
            var result = Convert(fit);
            Assert.That(result.Replaced,Is.EqualTo(1)); Assert.That(result.Shapes.Single().Type,Is.EqualTo(Kind.Box));
            var shape = result.Shapes[0]; var planes = ConvexHull.Build(raw.Vertices).Planes;
            foreach (int x in new[]{-1,1}) foreach (int y in new[]{-1,1}) foreach (int z in new[]{-1,1})
            {
                var corner = shape.Center+shape.Rotation*Vector3.Scale(shape.Size*.5f,new Vector3(x,y,z));
                var p = (new Vec3(corner.x,corner.y,corner.z)-fit.Center)/fit.Scale;
                Assert.That(planes.Max(plane => plane.At(p)),Is.LessThanOrEqualTo(.03));
            }
            Assert.That(raw.Vertices.Max(p => NativeDistance(shape,p*fit.Scale+fit.Center))/fit.Scale,Is.LessThanOrEqualTo(.03));
        }
        [Test] public void NativeApproximationUsesTheOriginalFitAndOneErrorAllowance()
        {
            var rounded = ConvexHull.Build(Numerics.SphereDirections(128).Select(p => p*.25).ToArray()).Mesh;
            var fit = new ProxyFit(new Vec3(),1,new[]{rounded}); var accepted = Convert(fit);
            Assert.That(accepted.Replaced,Is.EqualTo(1));
            Assert.That(accepted.Shapes[0].Type,Is.EqualTo(Kind.Sphere).Or.EqualTo(Kind.Capsule));
            if (accepted.Shapes[0].Type == Kind.Capsule)
                Assert.That(accepted.Shapes[0].Height,Is.LessThan(accepted.Shapes[0].Radius*2.1f),"a near-sphere may use a very short capsule when it has lower error");
            Assert.That(rounded.Vertices.Max(p=>NativeDistance(accepted.Shapes[0],p)),Is.LessThanOrEqualTo(.03));
            var enlarged = new TriangleMesh(rounded.Vertices.Select(p => p*1.1).ToArray(),rounded.Indices);
            var largerFit = new ProxyFit(new Vec3(),1,new[]{enlarged});
            var budgeted = Convert(largerFit);
            Assert.That(budgeted.Replaced,Is.EqualTo(1));
            Assert.That(enlarged.Vertices.Max(p=>NativeDistance(budgeted.Shapes[0],p)),Is.LessThanOrEqualTo(.03));
        }
        [Test] public void SuccessfulNativeShapeBypassesAnOtherwiseFailingVertexBudget()
        {
            var raw=ConvexHull.Build(Numerics.SphereDirections(128).Select(p=>p*.25).ToArray()).Mesh;
            var fit=new ProxyFit(new Vec3(),1,new[]{raw});var settings=Settings();settings.MaxHullVertices=4;
            var stages=new System.Collections.Generic.List<string>();
            var result=PickingPrimitiveApproximation.Export(fit,settings,default,stages.Add);
            Assert.That(result.Replaced,Is.EqualTo(1));Assert.That(result.Shapes[0].Type,Is.Not.EqualTo(Kind.Convex));
            Assert.That(stages,Does.Not.Contain("Exporting and cleaning convex meshes"));
            settings.PickingShapes=PickingShapeMode.ConvexOnly;
            Assert.Throws<InvalidOperationException>(()=>PickingPrimitiveApproximation.Export(fit,settings,default));
        }
        [Test] public void CapsuleLikeHullUsesOneNativeCapsule()
        {
            var points = Numerics.SphereDirections(128).SelectMany(p => new[]{p*.12+new Vec3(0,.23,0),p*.12-new Vec3(0,.23,0)}).ToArray();
            var raw = ConvexHull.Build(points).Mesh;
            var fit = new ProxyFit(new Vec3(),1,new[]{raw}); var result = Convert(fit);
            Assert.That(result.Replaced,Is.EqualTo(1)); Assert.That(result.Shapes[0].Type,Is.EqualTo(Kind.Capsule));
            Assert.That(raw.Vertices.Max(p => NativeDistance(result.Shapes[0],p)),Is.LessThanOrEqualTo(.03));
        }
        [Test] public void LooserNativeTolerancesAllowATaperedPieceWithoutChangingItsConvexBudget()
        {
            var points=(from y in new[]{-1,1} from x in new[]{-1,1} from z in new[]{-1,1}
                select new Vec3(x*(y < 0 ? .28 : .24),y*.3,z*.1)).ToArray();
            var fit=new ProxyFit(new Vec3(),1,new[]{ConvexHull.Build(points).Mesh});
            var settings=Settings();settings.NativeShapeErrorPercent=8;settings.NativeClickChangePercent=0;
            Assert.That(Convert(fit,settings).Replaced,Is.Zero,"exact click coverage rejects a taper-to-primitive substitution");
            settings.NativeClickChangePercent=20;
            var loose=Convert(fit,settings);
            Assert.That(loose.Replaced,Is.EqualTo(1));
            Assert.That(loose.MaximumDeviation,Is.LessThanOrEqualTo(.08));
            Assert.That(settings.MaxHullVertices,Is.EqualTo(24));Assert.That(settings.PickingInwardErrorPercent,Is.EqualTo(3));
            var preview=new ColliderGeneration.Preview{Settings=settings.Copy(),FitMatrix=Matrix4x4.identity,
                GeometryHash=new string('B',64),Fit=new PrimitiveColliderFitter.Result{Shapes=loose.Shapes}};
            var loaded=ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(preview));
            Assert.That(loaded.MatchesSettings(settings),Is.True);
            settings.NativeClickChangePercent=10;Assert.That(loaded.MatchesSettings(settings),Is.False);
            settings.NativeClickChangePercent=20;settings.NativeShapeErrorPercent=4;Assert.That(loaded.MatchesSettings(settings),Is.False);
        }
        [Test] public void UnsuitedTetrahedronStaysConvexAndAnExactBoxNeedsNoDeviation()
        {
            var tetra = ConvexHull.Build(new[]{new Vec3(.4,.4,.4),new Vec3(-.4,-.4,.4),new Vec3(-.4,.4,-.4),new Vec3(.4,-.4,-.4)}).Mesh;
            Assert.That(Convert(new ProxyFit(new Vec3(),1,new[]{tetra})).Replaced,Is.Zero);
            var box = Box(new Vec3(),new Vec3(.3,.2,.1),Quaternion.identity);
            var exact=Convert(new ProxyFit(new Vec3(),1,new[]{box}),Settings(0));
            Assert.That(exact.Replaced,Is.EqualTo(1));Assert.That(exact.MaximumDeviation,Is.LessThanOrEqualTo(1e-8));
        }
        [Serializable] sealed class ShapeSnapshot { public PrimitiveColliderFitter.Shape[] shapes; }
        [Test] public void IndependentNativeSearchesKeepDeterministicPieceOrder()
        {
            var pieces=Enumerable.Range(0,8).Select(i=>Box(new Vec3((i%4-1.5)*.2,(i/4-.5)*.3,0),
                new Vec3(.06,.1,.04),Quaternion.Euler(0,i*13,0))).ToArray();
            var fit=new ProxyFit(new Vec3(),1,pieces);
            var first=Convert(fit);var second=Convert(fit);
            Assert.That(first.Shapes.Length,Is.EqualTo(8));Assert.That(first.Replaced,Is.EqualTo(8));
            Assert.That(JsonUtility.ToJson(new ShapeSnapshot{shapes=second.Shapes}),
                Is.EqualTo(JsonUtility.ToJson(new ShapeSnapshot{shapes=first.Shapes})));
        }
        [Test] public void UGroupKeepsOpenSpaceAndCountWhileConvertingItsBars()
        {
            var bars = new[]{Box(new Vec3(-.3,0,0),new Vec3(.1,.4,.1),Quaternion.identity),
                Box(new Vec3(.3,0,0),new Vec3(.1,.4,.1),Quaternion.identity),Box(new Vec3(0,-.3,0),new Vec3(.2,.1,.1),Quaternion.identity)};
            var fit = new ProxyFit(new Vec3(),1,bars); var result = Convert(fit);
            Assert.That(result.Replaced,Is.EqualTo(3)); Assert.That(result.Shapes.Length,Is.EqualTo(3));
            foreach (var p in new[]{new Vector3(0,.2f,0),new Vector3(0,.45f,0)})
                Assert.That(result.Shapes.Any(s => PrimitiveColliderFitter.Contains(s,p)),Is.False);
            foreach (var p in new[]{new Vector3(-.3f,0,0),new Vector3(.3f,0,0),new Vector3(0,-.3f,0)})
                Assert.That(result.Shapes.Any(s => PrimitiveColliderFitter.Contains(s,p)),Is.True);
        }
        [Test] public void ConvexOnlyIsDeterministicAndCancellationDoesNotProduceAResult()
        {
            var fit = new ProxyFit(new Vec3(2,3,4),.7,new[]{Box(new Vec3(),new Vec3(.3,.2,.1),Quaternion.Euler(14,25,36))});
            var meshes = PickingProxyGenerator.Export(fit,new GenerationSettings{MaxHullVertices=24,MaxInwardError=.03},default,null);
            var keep = Convert(fit,Settings(mode:PickingShapeMode.ConvexOnly));
            Assert.That(keep.Replaced,Is.Zero); Assert.That(keep.Shapes[0].Type,Is.EqualTo(Kind.Convex));
            CollectionAssert.AreEqual(meshes[0].Vertices.Select(p => ((float)p.X,(float)p.Y,(float)p.Z)),
                keep.Shapes[0].ConvexVertices.Select(p => (p.x,p.y,p.z)));
            Assert.That(JsonUtility.ToJson(Convert(fit).Shapes[0]),Is.EqualTo(JsonUtility.ToJson(Convert(fit).Shapes[0])));
            Assert.Throws<OperationCanceledException>(() => PickingPrimitiveApproximation.Export(fit,Settings(),new CancellationToken(true)));
        }
        [Test] public void MixedNativeAndConvexResultRoundTripsWithoutLosingEitherGeometry()
        {
            var tetra = ConvexHull.Build(new[]{new Vec3(.48,.18,.18),new Vec3(.12,-.18,.18),new Vec3(.12,.18,-.18),new Vec3(.48,-.18,-.18)}).Mesh;
            var fit = new ProxyFit(new Vec3(),1,new[]{Box(new Vec3(-.3,0,0),new Vec3(.12,.18,.1),Quaternion.identity),tetra});
            var result = Convert(fit);
            Assert.That(result.Replaced,Is.EqualTo(1)); Assert.That(result.Shapes[0].Type,Is.EqualTo(Kind.Box)); Assert.That(result.Shapes[1].Type,Is.EqualTo(Kind.Convex));
            var preview = new ColliderGeneration.Preview { Settings = Settings(), FitMatrix = Matrix4x4.identity,
                GeometryHash = new string('A',64), Fit = new PrimitiveColliderFitter.Result { Shapes = result.Shapes } };
            var loaded = ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(preview));
            Assert.That(loaded.Count,Is.EqualTo(2)); Assert.That(loaded.NativeCount,Is.EqualTo(1)); Assert.That(loaded.MatchesSettings(Settings()));
            CollectionAssert.AreEqual(result.Shapes[1].ConvexVertices,loaded.Fit.Shapes[1].ConvexVertices);
            CollectionAssert.AreEqual(result.Shapes[1].ConvexTriangles,loaded.Fit.Shapes[1].ConvexTriangles);
        }
        [UnityTest] public IEnumerator PublicGenerationSnapshotAndApplyUseTheNativeCollider()
        {
            var root = new GameObject("native-picking-test"); var output = new GameObject("Output"); output.transform.SetParent(root.transform,false);
            var mesh = ColliderPreviewDisplay.BoxMesh(new Vector3(.6f,.4f,.2f));
            try
            {
                var request = ColliderGeneration.CreateRequest(output.transform,Settings()); request.Sources.Add(new ColliderMeshSource(mesh,root.transform));
                var task = ColliderGeneration.PreviewAsync(request); while (!task.IsCompleted) yield return null; if (task.IsFaulted) throw task.Exception;
                Assert.That(task.Result.NativeCount,Is.EqualTo(1));
                var loaded = ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(task.Result));
                Assert.That(loaded.NativeCount,Is.EqualTo(1)); Assert.That(loaded.MatchesSettings(request.Settings));
                var applied = ColliderGeneration.ApplyPreview(ColliderGeneration.RetargetPreview(loaded,request));
                Assert.That(applied.Colliders.Single(),Is.TypeOf<BoxCollider>());
                UnityEngine.Physics.SyncTransforms();
                Assert.That(applied.Colliders[0].Raycast(new Ray(new Vector3(0,0,-2),Vector3.forward),out var hit,4),Is.True);
                Assert.That(hit.distance,Is.EqualTo(1.9f).Within(1e-4));
                request.Settings.PickingShapes = PickingShapeMode.ConvexOnly;
                Assert.That(loaded.MatchesSettings(request.Settings),Is.False);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); }
        }
    }
}
