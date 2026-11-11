#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace July.Physics.Authoring.PickingProxy
{
    internal static class ConvexMeshExport
    {
        internal static TriangleMesh Clean(TriangleMesh input,double cell,CancellationToken token=default)
        {
            var original=input.Vertices;var current=ConvexHull.Build(original);
            // A separate sub-cell Euclidean bound is measured against ALL
            // original vertices after every removal; errors cannot accumulate.
            double tolerance=cell*.25;
            while(current.Mesh.Indices.Length/3>254)
            {
                var order=CornerOrder(current.Mesh);bool removed=false;double minimum=double.PositiveInfinity;
                foreach(int index in order)
                {
                    token.ThrowIfCancellationRequested();
                    var candidate=ConvexHull.Build(current.Mesh.Vertices.Where((p,i)=>i!=index).ToArray());
                    double deviation=InwardDeviation(original,candidate.Mesh,candidate.Planes,tolerance);
                    minimum=Math.Min(minimum,deviation);
                    if(deviation>tolerance)continue;
                    current=candidate;removed=true;break;
                }
                if(!removed)throw new InvalidOperationException($"Convex export needs more than {tolerance:R} inward tolerance ({current.Mesh.Vertices.Length} vertices/{current.Mesh.Indices.Length/3} triangles, tested minimum {minimum:R}).");
            }
            Validate(current.Mesh);return current.Mesh;
        }

        // The local one-ring planes only rank cheap candidates. Every proposed
        // removal is checked against the whole ORIGINAL polyhedron.
        private static int[] CornerOrder(TriangleMesh mesh)
        {
            var adjacent=new HashSet<int>[mesh.Vertices.Length];for(int i=0;i<adjacent.Length;i++)adjacent[i]=new HashSet<int>();
            for(int i=0;i<mesh.Indices.Length;i+=3)
            {int a=mesh.Indices[i],b=mesh.Indices[i+1],c=mesh.Indices[i+2];adjacent[a].Add(b);adjacent[a].Add(c);adjacent[b].Add(a);adjacent[b].Add(c);adjacent[c].Add(a);adjacent[c].Add(b);}
            var cost=new double[adjacent.Length];
            for(int i=0;i<cost.Length;i++)
            {
                var points=adjacent[i].Select(j=>mesh.Vertices[j]).ToArray();var center=new Vec3();foreach(var point in points)center+=point;center/=points.Length;
                var normal=Numerics.Pca(points)[2];cost[i]=Math.Abs(Vec3.Dot(mesh.Vertices[i]-center,normal));
            }
            return Enumerable.Range(0,cost.Length).OrderBy(i=>cost[i]).ThenBy(i=>i).ToArray();
        }

        internal static double InwardDeviation(Vec3[] original,TriangleMesh mesh,Plane3[] planes,double stopAbove=double.PositiveInfinity)
        {
            var query=new MeshQueries(mesh);double maximum=0;
            foreach(var point in original)
            {
                bool outside=false;foreach(var plane in planes)if(plane.At(point)>0){outside=true;break;}
                if(!outside)continue;
                maximum=Math.Max(maximum,query.Distance(point));if(maximum>stopAbove)return maximum;
            }
            return maximum;
        }

        internal static void Validate(TriangleMesh mesh, double maxEdgeError=0)
        {
            var edges=new Dictionary<(int,int),(int Count,int Balance)>();double volume=0;
            for(int i=0;i<mesh.Indices.Length;i+=3)
            {
                int a=mesh.Indices[i],b=mesh.Indices[i+1],c=mesh.Indices[i+2];var normal=Vec3.Cross(mesh.Vertices[b]-mesh.Vertices[a],mesh.Vertices[c]-mesh.Vertices[a]);
                if(normal.LengthSquared==0)throw new InvalidOperationException("Convex export contains a degenerate face.");
                var plane=Plane3.Triangle(mesh.Vertices[a],mesh.Vertices[b],mesh.Vertices[c]);
                foreach(var point in mesh.Vertices)
                {
                    bool outside;
                    if(maxEdgeError==0)outside=plane.At(point)>1e-7;
                    else
                    {
                        // Float-converted cached vertices are intervals around the
                        // original doubles. Bound the determinant perturbation,
                        // rather than amplifying it by dividing by a thin face area.
                        var u=mesh.Vertices[b]-mesh.Vertices[a];var v=mesh.Vertices[c]-mesh.Vertices[a];var w=point-mesh.Vertices[a];
                        double bound=maxEdgeError*(Vec3.Cross(v,w).Length+Vec3.Cross(w,u).Length+normal.Length)+
                            maxEdgeError*maxEdgeError*(u.Length+v.Length+w.Length)+maxEdgeError*maxEdgeError*maxEdgeError;
                        outside=Vec3.Dot(normal,w)>bound;
                    }
                    if(outside)throw new InvalidOperationException("Convex export contains a non-supporting face.");
                }
                volume+=Vec3.Dot(mesh.Vertices[a],Vec3.Cross(mesh.Vertices[b],mesh.Vertices[c]))/6;
                foreach(var pair in new[]{(a,b),(b,c),(c,a)})
                {var key=pair.Item1<pair.Item2?pair:(pair.Item2,pair.Item1);edges.TryGetValue(key,out var old);edges[key]=(old.Count+1,old.Balance+(pair.Item1<pair.Item2?1:-1));}
            }
            if(volume<=0||edges.Values.Any(e=>e.Count!=2||e.Balance!=0)||mesh.Vertices.Length-edges.Count+mesh.Indices.Length/3!=2)
                throw new InvalidOperationException("Convex export is not a closed oriented solid.");
        }
    }
}
