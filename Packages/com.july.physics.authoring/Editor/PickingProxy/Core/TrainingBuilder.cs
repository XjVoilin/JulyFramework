#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace July.Physics.Authoring.PickingProxy
{
    internal static class TrainingBuilder
    {
        internal static TrainingData Build(TriangleMesh mesh,PolicyField field,GenerationSettings settings,CancellationToken token,Action<string>? progress)
        {
            double h=field.Grid.Cell;var random=new Random(settings.Seed);var query=new MeshQueries(mesh);var directions=Numerics.SphereDirections(settings.TrainingViews);var body=new List<Vec3>(mesh.Vertices);var negative=new List<Vec3>();var rays=new List<TrainingRay>();
            int triangles=mesh.Indices.Length/3;var cumulative=new double[triangles];double sum=0;
            for(int i=0;i<triangles;i++){var a=mesh.Vertices[mesh.Indices[3*i]];var b=mesh.Vertices[mesh.Indices[3*i+1]];var c=mesh.Vertices[mesh.Indices[3*i+2]];body.Add((a+b+c)/3);sum+=Vec3.Cross(b-a,c-a).Length;cumulative[i]=sum;}
            for(int i=0;i<24000;i++)
            {int t=Array.BinarySearch(cumulative,random.NextDouble()*sum);if(t<0)t=~t;double u=random.NextDouble(),v=random.NextDouble();if(u+v>1){u=1-u;v=1-v;}var a=mesh.Vertices[mesh.Indices[3*t]];body.Add(a+u*(mesh.Vertices[mesh.Indices[3*t+1]]-a)+v*(mesh.Vertices[mesh.Indices[3*t+2]]-a));}
            var materialDistance=field.Grid.DistanceToFalse(field.Target);var free=field.Target.Select(x=>!x).ToArray();var freeDistance=field.Grid.DistanceToFalse(free);var fill=new List<Vec3>();var occupied=new List<Vec3>();var common=new List<Vec3>();
            for(int i=0;i<field.Grid.Count;i++){if(field.Target[i]){occupied.Add(field.Grid.Point(i));if(materialDistance[i]>1.25)fill.Add(field.Grid.Point(i));}if(field.B[i]&&!field.A[i]&&!field.Target[i]&&freeDistance[i]>2)common.Add(field.Grid.Point(i));}
            var visible=new bool[common.Count];progress?.Invoke("Building source-only common-cavity paths");
            foreach(var direction in directions)
            {
                token.ThrowIfCancellationRequested();var candidates=new List<int>();for(int i=0;i<common.Count;i++)if(!query.Occluded(common[i],direction)&&field.PathClear(common[i],direction,2)){visible[i]=true;candidates.Add(i);}
                // Partial Fisher-Yates: deterministic unique samples, bounded paths.
                int selected=Math.Min(1000,candidates.Count);for(int i=0;i<selected;i++){int j=random.Next(i,candidates.Count);int t=candidates[i];candidates[i]=candidates[j];candidates[j]=t;var end=common[candidates[i]];var start=end+2*direction;rays.Add(new TrainingRay(start,end,0,field.HullPlanes));AddSamples(start,-direction,2,5,field.HullPlanes,negative);}
            }
            for(int i=0;i<common.Count;i++)if(visible[i])negative.Add(common[i]);
            progress?.Invoke("Building source-only wide through-space");
            var capsules=new List<(Vec3 Start,Vec3 Direction,double Radius)>();
            var views=new ThroughSamples[directions.Length];
            Parallel.For(0,directions.Length,new ParallelOptions{CancellationToken=token,MaxDegreeOfParallelism=settings.Workers},
                view=>views[view]=BuildThroughView(directions[view],query,field,occupied,settings,token));
            // Commit in the original view order: ray indices and seeded fitting
            // batches remain identical regardless of worker completion order.
            for(int view=0;view<views.Length;view++)
            {
                rays.AddRange(views[view].Rays);negative.AddRange(views[view].Negative);capsules.AddRange(views[view].Capsules);
                if(view%10==0)progress?.Invoke($"Through-space views {view+1}/{directions.Length}");
            }
            foreach(var capsule in capsules)
            {
                token.ThrowIfCancellationRequested();var start=capsule.Start;var direction=capsule.Direction;double radius=capsule.Radius;rays.Add(new TrainingRay(start,start+4*direction,radius,field.HullPlanes));Numerics.SegmentInterval(start,direction,4,field.HullPlanes,out double lo,out double hi);int count=Math.Max(2,(int)Math.Ceiling((hi-lo)/(.5*h)));Numerics.ProjectionBasis(direction,out var x,out var y);
                for(int i=0;i<count;i++){var p=start+(lo+i*(hi-lo)/(count-1))*direction;negative.Add(p);for(int j=0;j<8;j++){double angle=j*Math.PI/4;negative.Add(p+.9*radius*(Math.Cos(angle)*x+Math.Sin(angle)*y));}}
            }
            var unique=new Dictionary<(long,long,long),Vec3>();foreach(var p in negative)
            {bool inside=true;foreach(var plane in field.HullPlanes)if(plane.At(p)>1e-7){inside=false;break;}if(inside){var key=((long)Math.Round(p.X*1e7),(long)Math.Round(p.Y*1e7),(long)Math.Round(p.Z*1e7));unique[key]=new Vec3(key.Item1*1e-7,key.Item2*1e-7,key.Item3*1e-7);}}
            progress?.Invoke($"Prepared {body.Count} body, {fill.Count} fill, {unique.Count} empty samples, {rays.Count} paths");
            return new TrainingData{Body=body.ToArray(),Fill=fill.ToArray(),Negative=unique.Values.OrderBy(p=>p.X).ThenBy(p=>p.Y).ThenBy(p=>p.Z).ToArray(),Rays=rays.ToArray(),HullPlanes=field.HullPlanes,Cell=h};
        }
        private sealed class ThroughSamples
        {
            internal readonly List<TrainingRay> Rays=new List<TrainingRay>();
            internal readonly List<Vec3> Negative=new List<Vec3>();
            internal readonly List<(Vec3 Start,Vec3 Direction,double Radius)> Capsules=new List<(Vec3,Vec3,double)>();
        }
        private static ThroughSamples BuildThroughView(Vec3 direction,MeshQueries query,PolicyField field,List<Vec3> occupied,GenerationSettings settings,CancellationToken token)
        {
            double h=field.Grid.Cell;int n=settings.ProjectionPixels;double step=1.3/n;
            var screen=new Grid3(n,n,1,new Vec3(),step);var result=new ThroughSamples();
            var rays=result.Rays;var negative=result.Negative;var capsules=result.Capsules;

                token.ThrowIfCancellationRequested();Numerics.ProjectionBasis(direction,out var x,out var y);var starts=new Vec3[n*n];var clear=new bool[n*n];var inside=new bool[n*n];
                for(int i=0;i<n;i++)for(int j=0;j<n;j++){int k=i*n+j;var start=((i+.5)*step-.65)*x+((j+.5)*step-.65)*y-2*direction;starts[k]=start;clear[k]=!query.Occluded(start,direction);inside[k]=Numerics.SegmentInterval(start,direction,4,field.HullPlanes,out _,out _);}
                var distance=screen.DistanceToFalse(clear);var interior=screen.DistanceToFalse(inside);var use=new bool[n*n];
                for(int k=0;k<use.Length;k++)
                {if(!clear[k]||!inside[k]||distance[k]*step<=2*h||interior[k]*step<=h||!field.PathClear(starts[k],direction,4))continue;use[k]=true;rays.Add(new TrainingRay(starts[k],starts[k]+4*direction,0,field.HullPlanes));AddSamples(starts[k],direction,4,17,field.HullPlanes,negative);}
                var visited=new bool[use.Length];var queue=new int[use.Length];
                for(int k=0;k<use.Length;k++)
                {
                    if(!use[k]||visited[k])continue;int head=0,tail=1,best=k;queue[0]=k;visited[k]=true;
                    while(head<tail){int q=queue[head++];if(distance[q]>distance[best])best=q;foreach(int j in screen.Neighbors(q))if(use[j]&&!visited[j]){visited[j]=true;queue[tail++]=j;}}
                    var start=starts[best];if(!Numerics.SegmentInterval(start,direction,4,field.HullPlanes,out double lo,out double hi))continue;double minimum=double.PositiveInfinity;
                    for(int j=0;j<129;j++)minimum=Math.Min(minimum,query.Distance(start+(lo+j*(hi-lo)/128)*direction));double radius=minimum-(hi-lo)/256-.5*h;
                    double materialDistanceToLine=double.PositiveInfinity;foreach(var p in occupied){double t=Math.Max(0,Math.Min(4,Vec3.Dot(p-start,direction)));materialDistanceToLine=Math.Min(materialDistanceToLine,(p-start-t*direction).Length);}radius=Math.Min(radius,materialDistanceToLine-Math.Sqrt(3)*.5*h);
                    if(radius>.25*h)capsules.Add((start,direction,radius));
                }


            return result;
        }
        private static void AddSamples(Vec3 start,Vec3 direction,double length,int count,Plane3[] hull,List<Vec3> output)
        {if(!Numerics.SegmentInterval(start,direction,length,hull,out double lo,out double hi))return;for(int i=0;i<count;i++){double fraction=.01+.98*i/(count-1);output.Add(start+(lo+fraction*(hi-lo))*direction);}}
    }

    public sealed class ProxyGenerationResult
    {
        public TriangleMesh[] ConvexMeshes {get;}
        public int RequestedMaximum {get;}
        public double OptimizationSeconds {get;}
        public int SourceVertices {get;}
        public string[] Notes {get;}
        internal readonly int TrainingRayCount, FittedConvexCount;
        internal ProxyGenerationResult(TriangleMesh[] meshes,int maximum,double seconds,int sourceVertices,int rays,int fitted,string[] notes)
        {ConvexMeshes=meshes;RequestedMaximum=maximum;OptimizationSeconds=seconds;SourceVertices=sourceVertices;TrainingRayCount=rays;FittedConvexCount=fitted;Notes=notes;}
    }
    public static class PickingProxyGenerator
    {
        public static ProxyGenerationResult Generate(TriangleMesh input,GenerationSettings settings,CancellationToken token=default,Action<string>? progress=null,IVisibilityBackend? visibilityBackend=null)
        {
            if(settings.MaxConvexes<1||settings.MaxConvexes>GenerationSettings.ConvexCountLimit||settings.Resolution<16||settings.LatitudeSamples<3||settings.LongitudeSamples<4||settings.LongitudeSamples%2!=0||settings.Workers<1||settings.FitSteps<0)throw new ArgumentException("Invalid generation settings: convex count must be 1–16.");
            if(settings.MaxHullVertices<4||settings.MaxHullVertices>GenerationSettings.HullVertexLimit||!double.IsFinite(settings.MaxInwardError)||settings.MaxInwardError<0||settings.MaxInwardError>GenerationSettings.InwardErrorLimit)throw new ArgumentException("Invalid generation settings: hull vertices must be 4–24 and inward error must be 0–20%.");
            var analysis=Analyze(input,settings,token,progress,visibilityBackend);
            var clock=System.Diagnostics.Stopwatch.StartNew();
            var fitted=Fit(analysis,settings,token,progress);
            var output=Export(fitted.Fit,settings,token,progress);
            return new ProxyGenerationResult(output,settings.MaxConvexes,clock.Elapsed.TotalSeconds,input.Vertices.Length,analysis.Training.Rays.Length,fitted.Model.Count,new[]{"Source-only generation; no previous proxy or external process.","Research candidate: inspect shape and picking before applying.",visibilityBackend==null?"Managed CPU backend; no Python/Torch dependency.":"Supplied GPU visibility backend with CPU precision verification; no Python/Torch/CUDA environment."});
        }
        internal static ProxyAnalysis Analyze(TriangleMesh input,GenerationSettings settings,CancellationToken token,Action<string>? progress,IVisibilityBackend? visibilityBackend)
        {
            var prepared=PrepareSource(input);
            var field=SourcePolicy.Build(prepared.Mesh,settings,token,progress,visibilityBackend);
            var data=TrainingBuilder.Build(prepared.Mesh,field,settings,token,progress);
            return new ProxyAnalysis(prepared.Center,prepared.Scale,field.HullMesh,field.HullPlanes,data);
        }
        internal static (ConvexModel Model,ProxyFit Fit) Fit(ProxyAnalysis analysis,GenerationSettings settings,CancellationToken token,Action<string>? progress)
        {
            var data=analysis.Training;ConvexModel model;
            bool directHull=data.Negative.Length==0&&data.Rays.Length==0;
            if(directHull)
            {
                var planes=analysis.HullPlanes;var c=new Vec3();foreach(var p in analysis.Hull.Vertices)c+=p;c/=analysis.Hull.Vertices.Length;var normals=new float[planes.Length*3];var radii=new float[planes.Length];for(int i=0;i<planes.Length;i++){for(int a=0;a<3;a++)normals[3*i+a]=(float)planes[i].Normal[a];radii[i]=(float)-planes[i].At(c);}model=new ConvexModel(1,planes.Length,new[]{(float)c.X,(float)c.Y,(float)c.Z},normals,radii);
            }
            else model=Optimizer.Fit(data,settings,token,progress);
            // Re-clipping H by its float-converted copies creates numerical sliver
            // faces. The no-empty-space case is exactly H, so export it directly.
            var meshes=directHull?new[]{analysis.Hull}:ExportModel(model,analysis.Hull);
            return (model,new ProxyFit(analysis.Center,analysis.Scale,meshes));
        }
        internal static TriangleMesh[] Export(ProxyFit fit,GenerationSettings settings,CancellationToken token,Action<string>? progress)
        {
            progress?.Invoke("Exporting and cleaning convex meshes");
            return fit.Meshes.Select(m=>ConvexVertexBudget.Reduce(m,settings.MaxHullVertices,settings.MaxInwardError,token))
                .Select(m=>new TriangleMesh(m.Vertices.Select(p=>p*fit.Scale+fit.Center).ToArray(),m.Indices)).ToArray();
        }
        internal static (TriangleMesh Mesh,Vec3 Center,double Scale) PrepareSource(TriangleMesh input)
        {
            var min=input.Vertices.Aggregate(Vec3.Min);var max=input.Vertices.Aggregate(Vec3.Max);var extent=max-min;double scale=Math.Max(extent.X,Math.Max(extent.Y,extent.Z));if(scale<1e-12)throw new ArgumentException("Mesh has zero size.");var center=(min+max)/2;
            // Exact position welding is numerical topology preparation, not shape editing.
            var positions=new List<Vec3>();var map=new Dictionary<(double,double,double),int>();var remap=new int[input.Vertices.Length];
            for(int i=0;i<input.Vertices.Length;i++){var p=input.Vertices[i];var key=(p.X,p.Y,p.Z);if(!map.TryGetValue(key,out int index)){index=positions.Count;map.Add(key,index);positions.Add((p-center)/scale);}remap[i]=index;}
            return(new TriangleMesh(positions.ToArray(),input.Indices.Select(i=>remap[i]).ToArray()),center,scale);
        }
        internal static TriangleMesh[] ExportModel(ConvexModel model,TriangleMesh hull)
        {
            var result=new List<TriangleMesh>();for(int k=0;k<model.Count;k++)
            {var shape=new Polyhedron(hull);bool valid=true;var center=new Vec3(model.Centers[k*3],model.Centers[k*3+1],model.Centers[k*3+2]);for(int j=0;j<model.Planes;j++){int p=k*model.Planes+j;var normal=new Vec3(model.Normals[p*3],model.Normals[p*3+1],model.Normals[p*3+2]).Normalized;if(!shape.Clip(new Plane3(normal,-Vec3.Dot(normal,center)-model.Radii[p]))){valid=false;break;}}if(valid)result.Add(shape.ToMesh());}
            if(result.Count==0)throw new InvalidOperationException("Optimization produced no volumetric convex region.");return result.ToArray();
        }
    }
}
