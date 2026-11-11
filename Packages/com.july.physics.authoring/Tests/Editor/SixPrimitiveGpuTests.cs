using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Context = July.Physics.Authoring.ColliderDecomposition.Silhouettes;
using Shape = July.Physics.Authoring.PrimitiveColliderFitter.Shape;

namespace July.Physics.Authoring
{
    public sealed class SixPrimitiveGpuTests
    {
        static Context Reference()
        {
            var mesh=ColliderPreviewDisplay.BoxMesh(Vector3.one*2);
            try{return new Context(mesh.vertices,mesh.triangles,Enumerable.Repeat(1f,mesh.vertexCount).ToArray(),default);}
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }

        static Shape Shape(int index, Context context)
        {
            var view=context.Views[0];
            var center=view.U*(view.Low.x+(32.5f+index%3)*view.Step.x)+view.V*(view.Low.y+36.5f*view.Step.y);
            return SixPrimitiveMeshes.Convert(new SixPrimitiveMath.Primitive
            {
                Kind=(SixPrimitiveMath.PrimitiveKind)(index%3),Center=center,
                Rotation=index<6?Quaternion.identity:Quaternion.Euler(17+index,31,9),
                HalfSize=new Vector3(.31f,.36f,.22f),Radius=view.Step.x*5,Height=.58f,Bottom=.32f,Top=.19f
            });
        }

        static void RequireGpu()
        {
            if(!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Requires a real compute-capable editor graphics device.");
        }

        [UnityTest]
        public IEnumerator NativeTypesMatchCpuMasksIncludingGridAlignedBoundaries()
        {
            RequireGpu();
            for(int index=0;index<24;index++)
            {
                var context=Reference();var shape=Shape(index,context);
                // Zero XOR against the CPU mask proves equality of every projected pixel.
                Array.Copy(context.Render(shape),context.Reference,context.WordCount);
                using(var gpu=ColliderGpuScoring.TryCreate(default))
                {
                    var task=Task.Run(()=>gpu.ScoreCandidates(context,new[]{new[]{shape}},new[]{new ulong[context.WordCount]}));
                    while(!task.IsCompleted)yield return null;
                    if(task.IsFaulted)throw task.Exception;
                    Assert.That(task.Result[0][0],Is.Zero,$"Fixture {index}: {shape.Type} GPU mask differs from CPU");
                }
            }
        }

        [UnityTest]
        public IEnumerator NativePairsAndCandidatesMatchCpuAcrossBatchBoundary()
        {
            RequireGpu();var context=Reference();
            var shapes=Enumerable.Range(6,6).Select(i=>Shape(i,context)).ToArray();
            var masks=shapes.Select(context.Render).ToArray();
            var trials=Enumerable.Range(0,129).Select(i=>new ColliderGpuScoring.CandidatePair
            {Left=shapes,Right=shapes,Background=i%2==0?new ulong[context.WordCount]:masks[2]}).ToArray();
            using(var gpu=ColliderGpuScoring.TryCreate(default))
            {
                var task=Task.Run(()=>gpu.Score(context,trials));
                while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception;
                for(int t=0;t<trials.Length;t++)for(int l=0;l<6;l++)for(int r=0;r<6;r++)
                {
                    var background=trials[t].Background.Select((word,j)=>word|masks[l][j]).ToArray();
                    Assert.That(task.Result[t][l*6+r],Is.EqualTo(context.Error(background,masks[r])),$"Pair {t}/{l}/{r}");
                }
                var singles=Task.Run(()=>gpu.ScoreCandidates(context,trials.Select(t=>t.Left).ToArray(),trials.Select(t=>t.Background).ToArray()));
                while(!singles.IsCompleted)yield return null;if(singles.IsFaulted)throw singles.Exception;
                for(int t=0;t<trials.Length;t++)for(int s=0;s<6;s++)
                    Assert.That(singles.Result[t][s],Is.EqualTo(context.Error(trials[t].Background,masks[s])),$"Candidate {t}/{s}");
                Assert.That(gpu.DispatchBatches,Is.EqualTo(4));
            }
        }

        [UnityTest]
        public IEnumerator CancellationAfterDispatchReleasesOwnedShader()
        {
            RequireGpu();
            int Owned()=>Resources.FindObjectsOfTypeAll<ComputeShader>().Count(s=>s.name.StartsWith("ColliderCandidateScores")&&s.hideFlags==HideFlags.HideAndDontSave);
            int before=Owned();var context=Reference();var shape=Shape(9,context);
            using(var cancellation=new CancellationTokenSource())
            using(var gpu=ColliderGpuScoring.TryCreate(cancellation.Token))
            {
                var groups=Enumerable.Range(0,257).Select(_=>new[]{shape}).ToArray();
                var backgrounds=groups.Select(_=>new ulong[context.WordCount]).ToArray();
                var task=Task.Run(()=>gpu.ScoreCandidates(context,groups,backgrounds));
                while(gpu.DispatchBatches==0 && !task.IsCompleted)yield return null;
                Assert.That(gpu.DispatchBatches,Is.GreaterThan(0));cancellation.Cancel();
                while(!task.IsCompleted)yield return null;
                Assert.That(task.IsCanceled || (task.IsFaulted && task.Exception.GetBaseException() is OperationCanceledException));
            }
            Assert.That(Owned(),Is.EqualTo(before));
        }
    }
}
