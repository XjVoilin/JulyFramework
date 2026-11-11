using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.Physics.Authoring
{
    public sealed class SixPrimitiveGpuParityTests
    {
        [Serializable] sealed class Input { public Vector3[] vertices;public int[] triangles; }
        [Serializable] sealed class Measurement
        {
            public string item,goal,device;
            public int shapes,gpuBatches;
            public double cpuSeconds,gpuSeconds;
            public bool identical;
        }

        internal static byte[] GeometryBytes(PrimitiveColliderFitter.Shape[] shapes)
        {
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
                {
                    void V(Vector3 v){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}
                    writer.Write(shapes.Length);
                    foreach(var s in shapes)
                    {
                        writer.Write((int)s.Type);V(s.Center);V(s.Size);
                        writer.Write(s.Rotation.x);writer.Write(s.Rotation.y);writer.Write(s.Rotation.z);writer.Write(s.Rotation.w);
                        writer.Write(s.Radius);writer.Write(s.Height);writer.Write(s.Bottom);writer.Write(s.Top);writer.Write(s.Volume);
                        writer.Write(s.ConvexVertices?.Length??-1);
                        if(s.ConvexVertices!=null)foreach(var v in s.ConvexVertices)V(v);
                        writer.Write(s.ConvexTriangles?.Length??-1);
                        if(s.ConvexTriangles!=null)foreach(int i in s.ConvexTriangles)writer.Write(i);
                    }
                }
                return stream.ToArray();
            }
        }

        static IEnumerator Compare(string name)
        {
            if(!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Requires a real compute-capable editor graphics device.");
            string fixture=Path.Combine(Application.dataPath,"GpuInputs",name+".json");
            if(!File.Exists(fixture))Assert.Ignore("Frozen fixtures are installed only in the isolated verification project.");
            var input=JsonUtility.FromJson<Input>(File.ReadAllText(fixture));
            var geometry=new ColliderGeometry.Geometry{Vertices=input.vertices,Indices=input.triangles};
            string folder="Library/SixGpuParity";Directory.CreateDirectory(folder);
            foreach(var goal in new[]{ColliderGeometryGoal.Envelope,ColliderGeometryGoal.Structure})
            {
                var settings=ColliderGenerationSettings.ForGoal(goal);
                Debug.Log($"[SixGpuParity] START {name} {goal} CPU");
                var cpu=Task.Run(()=>ColliderDecomposition.Fit(geometry,settings));
                while(!cpu.IsCompleted)yield return null;if(cpu.IsFaulted)throw cpu.Exception;
                var expected=GeometryBytes(cpu.Result.Shapes);
                Debug.Log($"[SixGpuParity] START {name} {goal} GPU; CPU {cpu.Result.Seconds:F2}s");
                using(var gpu=ColliderGpuScoring.TryCreate(CancellationToken.None))
                {
                    var task=Task.Run(()=>ColliderDecomposition.Fit(geometry,settings,null,default,gpu));
                    while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception;
                    var actual=GeometryBytes(task.Result.Shapes);
                    var measurement=new Measurement{item=name,goal=goal.ToString(),device=SystemInfo.graphicsDeviceName,
                        shapes=task.Result.Shapes.Length,gpuBatches=gpu.DispatchBatches,cpuSeconds=cpu.Result.Seconds,
                        gpuSeconds=task.Result.Seconds,identical=expected.SequenceEqual(actual)};
                    string path=Path.Combine(folder,name+"-"+goal);
                    File.WriteAllBytes(path+"-cpu.bin",expected);File.WriteAllBytes(path+"-gpu.bin",actual);
                    File.WriteAllText(path+".json",JsonUtility.ToJson(measurement));
                    Debug.Log("[SixGpuParity] "+JsonUtility.ToJson(measurement));
                    CollectionAssert.AreEqual(expected,actual,$"{name}/{goal} final primitive parameters or meshes changed");
                }
            }
        }

        [UnityTest,Timeout(600000)] public IEnumerator Backpack() => Compare("backpack");
        [UnityTest,Timeout(600000)] public IEnumerator Bowl() => Compare("bowl");
        [UnityTest,Timeout(600000)] public IEnumerator CallaLily() => Compare("calla_lily");
        [UnityTest,Timeout(600000)] public IEnumerator Gerbera() => Compare("gerbera");
        [UnityTest,Timeout(600000)] public IEnumerator Mug() => Compare("mug");
        [UnityTest,Timeout(600000)] public IEnumerator UShape() => Compare("u_shape");
    }
}
