using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.Physics.Authoring
{
    public sealed class SixPrimitiveGpuEntryTests
    {
        [Serializable] sealed class Input { public Vector3[] vertices;public int[] triangles; }
        [UnityTest]
        public IEnumerator PreviewEntryUsesAvailableBackendAndMatchesCpu()
        {
            // The U envelope fits immediately with one box and never dispatches
            // scoring. Use a curved fixture that exercises the GPU entry path.
            string path=Path.Combine(Application.dataPath,"GpuInputs/bowl.json");
            if(!File.Exists(path))Assert.Ignore("Isolated frozen fixture only.");
            var input=JsonUtility.FromJson<Input>(File.ReadAllText(path));
            var geometry=new ColliderGeometry.Geometry{Vertices=input.vertices,Indices=input.triangles};
            int Owned()=>Resources.FindObjectsOfTypeAll<ComputeShader>().Count(s=>s.name.StartsWith("ColliderCandidateScores")&&s.hideFlags==HideFlags.HideAndDontSave);
            int before=Owned();var root=new GameObject("six-backend-entry");
            var mesh=new Mesh{vertices=input.vertices,triangles=input.triangles};
            try
            {
                var settings=ColliderGenerationSettings.ForGoal(ColliderGeometryGoal.Envelope);
                var cpu=Task.Run(()=>PrimitiveColliderFitter.Fit(geometry,settings,null,default));
                while(!cpu.IsCompleted)yield return null;if(cpu.IsFaulted)throw cpu.Exception;
                var output=new GameObject("FineColliders");output.transform.SetParent(root.transform,false);output.layer=31;
                var request=ColliderGeneration.CreateRequest(output.transform,settings);
                request.Sources.Add(new ColliderMeshSource(mesh,root.transform));
                var task=ColliderGeneration.PreviewAsync(request);
                while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception;
                CollectionAssert.AreEqual(SixPrimitiveGpuParityTests.GeometryBytes(cpu.Result.Shapes),SixPrimitiveGpuParityTests.GeometryBytes(task.Result.Fit.Shapes));
                bool available=SystemInfo.supportsComputeShaders&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
                StringAssert.Contains(available?"GPU轮廓评分":"CPU",task.Result.Summary);
                Assert.That(Owned(),Is.EqualTo(before));
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);}
        }
    }
}
