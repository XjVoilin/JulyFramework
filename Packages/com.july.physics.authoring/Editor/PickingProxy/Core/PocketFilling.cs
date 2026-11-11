#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace July.Physics.Authoring.PickingProxy
{
    internal static class PocketFilling
    {
        private sealed class Candidate
        {internal int[] Faces=Array.Empty<int>();internal Plane3[] Planes=Array.Empty<Plane3>();internal double Volume;internal Vec3 Min,Max;}
        internal static int Apply(TriangleMesh mesh,PolicyField field,CancellationToken token)
        {
            var v=mesh.Vertices;var f=mesh.Indices;int count=f.Length/3;var normals=new Vec3[count];var areas=new double[count];var centers=new Vec3[count];var edges=new Dictionary<(int,int),List<int>>();
            for(int i=0;i<count;i++)
            {
                int a=f[3*i],b=f[3*i+1],c=f[3*i+2];var n=Vec3.Cross(v[b]-v[a],v[c]-v[a]);double length=n.Length;normals[i]=length>1e-14?n/length:new Vec3();areas[i]=length/2;centers[i]=(v[a]+v[b]+v[c])/3;
                foreach(var e in new[]{(a,b),(b,c),(c,a)}){var key=e.Item1<e.Item2?e:(e.Item2,e.Item1);if(!edges.TryGetValue(key,out var list)){list=new List<int>();edges.Add(key,list);}list.Add(i);}
            }
            var candidates=new List<Candidate>();var seen=new HashSet<string>();
            foreach(double barrier in new[]{0.0,10.0,25.0})
            {
                var parent=Enumerable.Range(0,count).ToArray();int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
                foreach(var pair in edges.Values)if(pair.Count==2)
                {int a=pair[0],b=pair[1];double dot=Math.Max(-1,Math.Min(1,Vec3.Dot(normals[a],normals[b]))),angle=Math.Acos(dot);if(Vec3.Dot(normals[a],centers[b]-centers[a])>1e-11)angle=-angle;if(angle<=barrier*Math.PI/180+1e-10)parent[Root(a)]=Root(b);}
                foreach(var group in Enumerable.Range(0,count).GroupBy(Root))
                {
                    token.ThrowIfCancellationRequested();var ids=group.ToArray();if(ids.Length<4||!seen.Add(string.Join(",",ids)))continue;var vertices=new HashSet<int>();var counts=new Dictionary<(int,int),(int Count,int A,int B)>();
                    foreach(int i in ids)for(int k=0;k<3;k++)
                    {int a=f[3*i+k],b=f[3*i+(k+1)%3];vertices.Add(a);var key=a<b?(a,b):(b,a);counts.TryGetValue(key,out var old);counts[key]=(old.Count+1,a,b);}
                    if(vertices.Count-counts.Count+ids.Length!=1)continue;var boundary=counts.Values.Where(x=>x.Count==1).ToArray();if(boundary.Length<3||boundary.Select(x=>x.A).Distinct().Count()!=boundary.Length||boundary.Select(x=>x.B).Distinct().Count()!=boundary.Length)continue;
                    var follows=boundary.ToDictionary(x=>x.A,x=>x.B);var loop=new List<int>{follows.Keys.Min()};bool disk=true;
                    while(follows.TryGetValue(loop[loop.Count-1],out int next)&&next!=loop[0]){if(loop.Contains(next)){disk=false;break;}loop.Add(next);}
                    if(!disk||loop.Count!=boundary.Length)continue;var rim=loop.Select(i=>v[i]).ToArray();var center=new Vec3();foreach(var p in rim)center+=p;center/=rim.Length;var basis=Numerics.Pca(rim);var normal=basis[2];var average=new Vec3();foreach(int i in ids)average+=normals[i]*areas[i];if(Vec3.Dot(normal,average)<0)normal=-normal;
                    var interior=vertices.Except(loop).ToArray();double depth=interior.Length==0?0:interior.Max(i=>-Vec3.Dot(v[i]-center,normal));if(depth<.5*field.Grid.Cell)continue;
                    var x=basis[0];var y=Vec3.Cross(normal,x);var rim2=rim.Select(p=>new Vec3(Vec3.Dot(p-center,x),Vec3.Dot(p-center,y),0)).ToArray();var polygon=Numerics.Hull2D(rim2);if(polygon.Length<3)continue;
                    double Area(int[] order){double area=0;for(int i=0;i<order.Length;i++){var a=rim2[order[i]];var b=rim2[order[(i+1)%order.Length]];area+=a.X*b.Y-b.X*a.Y;}return Math.Abs(area)/2;}
                    if(Area(Enumerable.Range(0,rim.Length).ToArray())/Area(polygon)<.95)continue;var points=vertices.Select(i=>v[i]).ToArray();bool footprint=true;
                    foreach(var p in points)
                    {var q=new Vec3(Vec3.Dot(p-center,x),Vec3.Dot(p-center,y),0);for(int k=0;k<polygon.Length;k++){var a=rim2[polygon[k]];var b=rim2[polygon[(k+1)%polygon.Length]];if((b.X-a.X)*(q.Y-a.Y)-(b.Y-a.Y)*(q.X-a.X)<-1e-8){footprint=false;break;}}if(!footprint)break;}
                    if(!footprint)continue;double rimError=rim.Max(p=>Math.Abs(Vec3.Dot(p-center,normal)));Plane3[] roof;
                    if(rimError<1e-8)roof=new[]{new Plane3(normal,-Vec3.Dot(normal,center))};else roof=ConvexHull.Build(rim).Planes.Where(p=>Vec3.Dot(p.Normal,normal)>1e-7).ToArray();
                    if(roof.Length==0||points.Any(p=>roof.Any(plane=>plane.At(p)>1e-8)))continue;
                    // Reject a flat patch before asking the 3D hull builder.
                    var pca=Numerics.Pca(points);var pmean=new Vec3();foreach(var p in points)pmean+=p;pmean/=points.Length;
                    if(points.Max(p=>Math.Abs(Vec3.Dot(p-pmean,pca[2])))<1e-8)continue;var hull=ConvexHull.Build(points);double volume=0;for(int i=0;i<hull.Mesh.Indices.Length;i+=3)volume+=Vec3.Dot(hull.Mesh.Vertices[hull.Mesh.Indices[i]],Vec3.Cross(hull.Mesh.Vertices[hull.Mesh.Indices[i+1]],hull.Mesh.Vertices[hull.Mesh.Indices[i+2]]))/6;volume=Math.Abs(volume);
                    if(volume<Math.Pow(field.Grid.Cell,3))continue;candidates.Add(new Candidate{Faces=ids,Planes=hull.Planes,Volume=volume,Min=points.Aggregate(Vec3.Min),Max=points.Aggregate(Vec3.Max)});
                }
            }
            var used=new HashSet<int>();int applied=0;foreach(var candidate in candidates.OrderByDescending(x=>x.Volume))
            {
                if(candidate.Faces.Any(used.Contains))continue;foreach(int i in candidate.Faces)used.Add(i);
                for(int i=0;i<field.Grid.Count;i++)if(field.Hull[i]&&!field.Target[i])
                {var p=field.Grid.Point(i);if(p.X<candidate.Min.X||p.Y<candidate.Min.Y||p.Z<candidate.Min.Z||p.X>candidate.Max.X||p.Y>candidate.Max.Y||p.Z>candidate.Max.Z)continue;if(candidate.Planes.All(plane=>plane.At(p)<=1e-8))field.Target[i]=true;}applied++;
            }
            return applied;
        }
    }
}
