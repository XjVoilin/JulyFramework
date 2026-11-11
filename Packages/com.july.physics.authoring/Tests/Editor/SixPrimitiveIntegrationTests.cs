using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace July.Physics.Authoring
{
    public sealed class SixPrimitiveIntegrationTests
    {
        static PrimitiveColliderFitter.Shape Shape(SixPrimitiveMath.PrimitiveKind kind) =>
            SixPrimitiveMeshes.Convert(new SixPrimitiveMath.Primitive
            {
                Kind=kind, Center=new Vector3(.4f,-.2f,.1f),Rotation=Quaternion.Euler(17,31,9),
                HalfSize=new Vector3(.7f,1,.3f),Radius=.6f,Height=2,Bottom=.8f,Top=.3f
            });

        static ColliderGeneration.Preview Snapshot(PrimitiveColliderFitter.Shape shape) => new ColliderGeneration.Preview
        {
            Settings=ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Structure),FitMatrix=Matrix4x4.identity,
            GeometryHash=new string('a',64),Fit=new PrimitiveColliderFitter.Result{Shapes=new[]{shape},Note="typed fixture"}
        };

        [Test]
        public void SixTypeSnapshotsRejectPreviousSchemeAndReservedConvexValue()
        {
            var shape=Shape(SixPrimitiveMath.PrimitiveKind.Prism);
            shape.Height=0; // Prism height is carried by Size.y, as in automatic generation.
            var json=ColliderGeneration.SerializePreview(Snapshot(shape));
            Assert.That(ColliderGeneration.DeserializePreview(json).Fit.Shapes[0].Type,Is.EqualTo(shape.Type));
            Assert.Throws<InvalidDataException>(()=>ColliderGeneration.DeserializePreview(json.Replace("\"version\":2","\"version\":1")));
            shape.Type=(PrimitiveColliderFitter.Kind)3;
            Assert.Throws<InvalidDataException>(()=>ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(Snapshot(shape))));
        }

        [UnityTest]
        public IEnumerator NativeCapsuleAppliesInCompensatedFrame()
        {
            var root=new GameObject("scaled-six-type-test");
            var source=ColliderPreviewDisplay.BoxMesh(Vector3.one);
            try
            {
                root.transform.localScale=new Vector3(1.2f,.9f,.8f);
                root.transform.localRotation=Quaternion.Euler(13,29,7);
                var output=new GameObject("PhysicalShapes");output.layer=30;
                output.transform.SetParent(root.transform,false);
                output.transform.localRotation=Quaternion.Euler(23,-11,17);
                var request=ColliderGeneration.CreateRequest(output.transform,ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Envelope));
                request.Sources.Add(new ColliderMeshSource(source,root.transform));
                var task=ColliderGeneration.PreviewAsync(request);
                while(!task.IsCompleted)yield return null;
                if(task.IsFaulted)throw task.Exception;
                var preview=task.Result;var shape=Shape(SixPrimitiveMath.PrimitiveKind.Capsule);
                preview.Fit.Shapes=new[]{shape};
                var expected=preview.FitMatrix*Matrix4x4.TRS(shape.Center,shape.Rotation,Vector3.one);
                var restored=ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(preview));
                var applied=ColliderGeneration.ApplyPreview(ColliderGeneration.RetargetPreview(restored,request));
                var collider=(CapsuleCollider)applied.Colliders[0];
                foreach(var p in new[]{Vector3.zero,Vector3.up*shape.Height*.5f,Vector3.right*shape.Radius})
                    Assert.That(Vector3.Distance(collider.transform.TransformPoint(p),expected.MultiplyPoint3x4(p)),Is.LessThan(1e-4f));
            }
            finally{Object.DestroyImmediate(root);Object.DestroyImmediate(source);}
        }

        [UnityTest]
        public IEnumerator NativeTypesApplyAndLegacyRegularPreviewsRequireRegeneration()
        {
            foreach(SixPrimitiveMath.PrimitiveKind kind in Enum.GetValues(typeof(SixPrimitiveMath.PrimitiveKind)))
            {
                var root=new GameObject("six-type-test");
                Mesh source=null,display=null;
                try
                {
                    var shape=Shape(kind);
                    source=shape.Type==PrimitiveColliderFitter.Kind.Box?ColliderPreviewDisplay.BoxMesh(shape.Size):
                        SixPrimitiveMeshes.Regular(shape)?new Mesh{vertices=shape.ConvexVertices,triangles=shape.ConvexTriangles}:
                        ColliderPreviewDisplay.RoundMesh(shape.Radius,shape.Type==PrimitiveColliderFitter.Kind.Sphere?shape.Radius*2:shape.Height);
                    source.RecalculateBounds();
                    var visual=new GameObject("visual");visual.transform.SetParent(root.transform,false);
                    visual.transform.localPosition=shape.Center;visual.transform.localRotation=shape.Rotation;
                    var output=new GameObject("PhysicalShapes");output.transform.SetParent(root.transform,false);output.layer=30;
                    var old=new GameObject("old");old.transform.SetParent(output.transform,false);old.AddComponent<BoxCollider>();
                    var request=ColliderGeneration.CreateRequest(output.transform,ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Envelope));
                    request.Sources.Add(new ColliderMeshSource(source,visual.transform));
                    var task=ColliderGeneration.PreviewAsync(request);
                    while(!task.IsCompleted)yield return null;
                    if(task.IsFaulted)throw task.Exception;
                    // Typed adapter fixtures are independent of which primitive the fitter prefers.
                    // Representative baseline tests separately exercise automatic generation.
                    var preview=task.Result;preview.Fit.Shapes=new[]{shape};
                    var restored=ColliderGeneration.DeserializePreview(ColliderGeneration.SerializePreview(preview));
                    display=ColliderGeneration.CreatePreviewMesh(restored,Matrix4x4.identity);
                    Assert.That(display.vertexCount,Is.GreaterThan(0));
                    if(SixPrimitiveMeshes.Regular(shape))
                    {
                        Assert.That(restored.MatchesSettings(request.Settings),Is.False);
                        Assert.Throws<InvalidOperationException>(()=>ColliderGeneration.RetargetPreview(restored,request));
                        Assert.That(old!=null);continue;
                    }
                    var rebound=ColliderGeneration.RetargetPreview(restored,request);
                    var applied=ColliderGeneration.ApplyPreview(rebound);
                    Assert.That(applied.Colliders.Length,Is.EqualTo(1));
                    var collider=applied.Colliders[0];
                    Assert.That(collider.gameObject.layer,Is.EqualTo(30));
                    Assert.That(old==null);
                    Assert.That(PrimitiveColliderFitter.Contains(shape,shape.Center));
                    Assert.That(collider.GetType().Name,Is.EqualTo(kind+"Collider"));
                    UnityEngine.Physics.SyncTransforms();
                    float distance=collider.bounds.extents.magnitude*3+1;
                    Assert.That(collider.Raycast(new Ray(shape.Center-collider.transform.forward*distance,collider.transform.forward),out _,distance*2));
                    var appliedScale = collider.transform.localScale;
                    var appliedMesh = (collider as MeshCollider)?.sharedMesh;
                    Undo.PerformUndo();Assert.That(output.transform.Find("old")!=null);
                    Undo.PerformRedo();Assert.That(output.GetComponentsInChildren<Collider>().Length,Is.EqualTo(1));
                    var redone = output.GetComponentInChildren<Collider>();
                    Assert.That(redone.transform.localScale, Is.EqualTo(appliedScale));
                    if (appliedMesh != null) Assert.That(((MeshCollider)redone).sharedMesh, Is.EqualTo(appliedMesh));
                }
                finally
                {
                    if(display!=null)Object.DestroyImmediate(display);
                    Object.DestroyImmediate(root);
                    if(source!=null)Object.DestroyImmediate(source);
                }
            }
        }
    }
}
