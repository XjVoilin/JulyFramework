#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace July.Physics.Authoring.PickingProxy
{
    public sealed class GenerationSettings
    {
        public const int DefaultConvexCount=8,ConvexCountLimit=16,DefaultHullVertices=16,HullVertexLimit=24;
        public const double DefaultInwardError=.1,InwardErrorLimit=.2;
        public int MaxConvexes=DefaultConvexCount,Resolution=96,LatitudeSamples=25,LongitudeSamples=48,FitSteps=9000,Seed=1102026;
        public int Workers=Math.Min(8,Environment.ProcessorCount);
        public int TrainingViews=37,ProjectionPixels=128;
        public int MaxHullVertices=DefaultHullVertices;
        public double MaxInwardError=DefaultInwardError;
    }
    internal sealed class PolicyField
    {
        internal readonly Grid3 Grid;
        internal readonly bool[] Hull,Material,Free,A,B,Target;
        internal readonly int[] Seed0,Seed1;
        internal readonly Vec3[] Directions;
        internal readonly TriangleMesh HullMesh;
        internal readonly Plane3[] HullPlanes;
        internal int PocketCount;
        internal PolicyField(Grid3 grid,TriangleMesh hull,Plane3[] planes,Vec3[] directions)
        {Grid=grid;HullMesh=hull;HullPlanes=planes;Directions=directions;Hull=new bool[grid.Count];Material=new bool[grid.Count];Free=new bool[grid.Count];A=new bool[grid.Count];B=new bool[grid.Count];Target=new bool[grid.Count];Seed0=Enumerable.Repeat(-1,grid.Count).ToArray();Seed1=(int[])Seed0.Clone();}
        internal bool PathClear(Vec3 start,Vec3 direction,double length)
        {
            if(!Numerics.SegmentInterval(start,direction,length,HullPlanes,out double lo,out double hi))return true;
            for(double t=lo;t<hi;t+=Grid.Cell*.45){int index=Grid.At(start+t*direction);if(index>=0&&Target[index])return false;}return true;
        }
    }
    internal static class SourcePolicy
    {
        private sealed class Work
        {internal readonly bool[] Visible;internal readonly int[] Labels,Queue;internal Work(int n){Visible=new bool[n];Labels=new int[n];Queue=new int[n];}}
        internal static PolicyField Build(TriangleMesh mesh,GenerationSettings settings,CancellationToken token,Action<string>? progress,IVisibilityBackend? visibilityBackend=null)
        {
            var hull=ConvexHull.Build(mesh.Vertices);var min=mesh.Vertices.Aggregate(Vec3.Min);var max=mesh.Vertices.Aggregate(Vec3.Max);double h=1.0/settings.Resolution;var extent=max-min;var grid=new Grid3((int)Math.Ceiling(extent.X/h)+4,(int)Math.Ceiling(extent.Y/h)+4,(int)Math.Ceiling(extent.Z/h)+4,min-new Vec3(2,2,2)*h,h);var query=new MeshQueries(mesh);
            int lat=settings.LatitudeSamples,lon=settings.LongitudeSamples;var dirs=new Vec3[lat*lon];var weights=new double[dirs.Length];var opposite=new int[dirs.Length];
            for(int r=0;r<lat;r++)for(int c=0;c<lon;c++)
            {double theta=r*Math.PI/(lat-1),phi=c*2*Math.PI/lon,dt=Math.PI/(lat-1);int i=r*lon+c;dirs[i]=new Vec3(Math.Sin(theta)*Math.Cos(phi),Math.Sin(theta)*Math.Sin(phi),Math.Cos(theta));weights[i]=(Math.Cos(Math.Max(0,theta-dt/2))-Math.Cos(Math.Min(Math.PI,theta+dt/2)))*2*Math.PI/lon;opposite[i]=(lat-1-r)*lon+(c+lon/2)%lon;}
            var field=new PolicyField(grid,hull.Mesh,hull.Planes,dirs);var options=new ParallelOptions{CancellationToken=token,MaxDegreeOfParallelism=settings.Workers};progress?.Invoke("Rasterizing source solid");
            Parallel.For(0,grid.Count,options,i=>
            {
                var p=grid.Point(i);foreach(var plane in hull.Planes)if(plane.At(p)>1e-8)return;field.Hull[i]=true;double d=query.Distance(p);bool occupied=query.Inside(p);field.Material[i]=occupied;field.Free[i]=!occupied&&d>.65*h;
            });
            progress?.Invoke("Classifying source visibility patches");var ids=Enumerable.Range(0,grid.Count).Where(i=>field.Free[i]).ToArray();
            VisibilityMask? accelerated=null;
            if(visibilityBackend!=null)
            {
                accelerated=visibilityBackend.Compute(new VisibilityGeometry(query),ids.Select(grid.Point).ToArray(),dirs,token,progress);
                if(accelerated.PointCount!=ids.Length||accelerated.DirectionCount!=dirs.Length)throw new InvalidOperationException("Accelerator returned mismatched visibility dimensions.");
            }
            Parallel.For(0,ids.Length,options,()=>new Work(dirs.Length),(position,state,work)=>
            {
                int index=ids[position];var p=grid.Point(index);for(int k=0;k<dirs.Length;k++)work.Visible[k]=accelerated==null||accelerated.Uncertain(position,k)?!query.Occluded(p,dirs[k]):accelerated.Visible(position,k);for(int k=0;k<dirs.Length;k++)if(work.Visible[k]&&work.Visible[opposite[k]]){field.A[index]=true;break;}
                Array.Clear(work.Labels,0,work.Labels.Length);int label=0;double first=0,second=0;int firstSeed=-1,secondSeed=-1;
                for(int k=0;k<dirs.Length;k++)
                {
                    if(!work.Visible[k]||work.Labels[k]!=0)continue;label++;int head=0,tail=1;work.Queue[0]=k;work.Labels[k]=label;double area=0;var sum=new Vec3();
                    while(head<tail)
                    {
                        int q=work.Queue[head++],r=q/lon,c=q%lon;area+=weights[q];sum+=dirs[q]*weights[q];
                        void Add(int j){if(work.Visible[j]&&work.Labels[j]==0){work.Labels[j]=label;work.Queue[tail++]=j;}}
                        if(r>0)Add(q-lon);if(r+1<lat)Add(q+lon);Add(r*lon+(c+lon-1)%lon);Add(r*lon+(c+1)%lon);
                    }
                    if(area<.002)continue;int seed=work.Queue[0];double best=double.NegativeInfinity;for(int j=0;j<tail;j++){int q=work.Queue[j];double dot=Vec3.Dot(dirs[q],sum);if(dot>best){best=dot;seed=q;}}
                    if(area>first){second=first;secondSeed=firstSeed;first=area;firstSeed=seed;}else if(area>second){second=area;secondSeed=seed;}
                }
                if(secondSeed>=0){field.B[index]=true;field.Seed0[index]=firstSeed;field.Seed1[index]=secondSeed;}return work;
            },_=>{});
            progress?.Invoke("Gating common space and preserving witness paths");var visited=new bool[grid.Count];var queue=new int[grid.Count];
            foreach(int index in ids)
            {
                token.ThrowIfCancellationRequested();if(visited[index])continue;int head=0,tail=1;queue[0]=index;visited[index]=true;bool supported=false;
                while(head<tail){int q=queue[head++];supported|=field.A[q];foreach(int neighbor in grid.Neighbors(q))if(field.Free[neighbor]&&!visited[neighbor]){visited[neighbor]=true;queue[tail++]=neighbor;}}
                if(!supported)for(int j=0;j<tail;j++)field.B[queue[j]]=false;
            }
            var keep=new bool[grid.Count];foreach(int index in ids){keep[index]=field.A[index]||field.B[index];if(!field.B[index])continue;var p=grid.Point(index);foreach(int seed in new[]{field.Seed0[index],field.Seed1[index]}){var dir=dirs[seed];if(!Numerics.SegmentInterval(p,dir,2,hull.Planes,out double lo,out double hi))continue;for(double t=0;t<=hi;t+=.45*h){int j=grid.At(p+t*dir);if(j>=0)keep[j]=true;}}}
            for(int i=0;i<grid.Count;i++)field.Target[i]=field.Material[i]||(field.Hull[i]&&!keep[i]);
            progress?.Invoke("Abstracting common-space boundary");var freeMask=field.Target.Select(x=>!x).ToArray();var distance=grid.DistanceToFalse(freeMask);var common=Enumerable.Range(0,grid.Count).Where(i=>field.B[i]&&!field.A[i]&&!field.Target[i]&&distance[i]>2).Select(grid.Point).ToArray();
            if(common.Length>=4)
            {
                var basis=Numerics.Pca(common);var normals=new List<Vec3>();for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++){int n=(x!=0?1:0)+(y!=0?1:0)+(z!=0?1:0);if(n==1||n==3)normals.Add((x*basis[0]+y*basis[1]+z*basis[2]).Normalized);}
                var supports=normals.Select(n=>common.Max(p=>Vec3.Dot(n,p))+.25*h).ToArray();
                for(int i=0;i<grid.Count;i++)
                {if(!field.Hull[i]||field.Material[i])continue;var p=grid.Point(i);bool within=true;for(int k=0;k<normals.Count;k++)if(Vec3.Dot(normals[k],p)>supports[k]+1e-8){within=false;break;}bool retained=keep[i]&&(field.A[i]||distance[i]>2||within);field.Target[i]=!retained;}
            }
            progress?.Invoke("Filling local rim-bounded pockets");field.PocketCount=PocketFilling.Apply(mesh,field,token);return field;
        }
    }
}
