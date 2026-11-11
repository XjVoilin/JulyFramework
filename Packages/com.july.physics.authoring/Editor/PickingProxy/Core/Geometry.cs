#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace July.Physics.Authoring.PickingProxy
{
    public readonly struct Vec3
    {
        public readonly double X,Y,Z;
        public Vec3(double x,double y,double z){X=x;Y=y;Z=z;}
        public double this[int i]=>i==0?X:i==1?Y:Z;
        public double LengthSquared=>Dot(this,this);
        public double Length=>Math.Sqrt(LengthSquared);
        public Vec3 Normalized=>this/Length;
        public static Vec3 operator +(Vec3 a,Vec3 b)=>new Vec3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static Vec3 operator -(Vec3 a,Vec3 b)=>new Vec3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static Vec3 operator -(Vec3 a)=>new Vec3(-a.X,-a.Y,-a.Z);
        public static Vec3 operator *(Vec3 a,double b)=>new Vec3(a.X*b,a.Y*b,a.Z*b);
        public static Vec3 operator *(double b,Vec3 a)=>a*b;
        public static Vec3 operator /(Vec3 a,double b)=>a*(1/b);
        public static double Dot(Vec3 a,Vec3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        public static Vec3 Cross(Vec3 a,Vec3 b)=>new Vec3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        public static Vec3 Min(Vec3 a,Vec3 b)=>new Vec3(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Min(a.Z,b.Z));
        public static Vec3 Max(Vec3 a,Vec3 b)=>new Vec3(Math.Max(a.X,b.X),Math.Max(a.Y,b.Y),Math.Max(a.Z,b.Z));
    }

    public readonly struct Plane3
    {
        public readonly Vec3 Normal;
        public readonly double Offset;
        public Plane3(Vec3 normal,double offset){Normal=normal;Offset=offset;}
        public double At(Vec3 p)=>Vec3.Dot(Normal,p)+Offset;
        public static Plane3 Triangle(Vec3 a,Vec3 b,Vec3 c)
        {var n=Vec3.Cross(b-a,c-a).Normalized;return new Plane3(n,-Vec3.Dot(n,a));}
    }

    public sealed class TriangleMesh
    {
        public Vec3[] Vertices {get;}
        public int[] Indices {get;}
        public TriangleMesh(Vec3[] vertices,int[] indices)
        {
            if(vertices==null||indices==null||vertices.Length<4||indices.Length<12||indices.Length%3!=0)throw new ArgumentException("A 3D triangle mesh is required.");
            foreach(var p in vertices)if(double.IsNaN(p.X)||double.IsInfinity(p.X)||double.IsNaN(p.Y)||double.IsInfinity(p.Y)||double.IsNaN(p.Z)||double.IsInfinity(p.Z))throw new ArgumentException("Non-finite mesh position.");
            foreach(int i in indices)if(i<0||i>=vertices.Length)throw new ArgumentException("Triangle index out of range.");
            Vertices=vertices;Indices=indices;
        }
    }

    internal sealed class HullFace
    {
        internal readonly int A,B,C;
        internal readonly Plane3 Plane;
        internal HullFace(int a,int b,int c,Vec3[] v,Vec3 interior)
        {if(ConvexHull.Orientation(v[a],v[b],v[c],interior)>0){int t=b;b=c;c=t;}A=a;B=b;C=c;Plane=Plane3.Triangle(v[a],v[b],v[c]);}
    }

    internal static class ConvexHull
    {
        // The clipping output contains almost coplanar corners. A distance
        // epsilon can select an inconsistent set of visible faces and corrupt
        // the horizon. Resolve near-zero orientation using the exact binary
        // input coordinates instead of perturbing the mesh.
        internal static int Orientation(Vec3 a,Vec3 b,Vec3 c,Vec3 d)
        {
            var x=b-a;var y=c-a;var z=d-a;
            double determinant=Vec3.Dot(Vec3.Cross(x,y),z);
            double permanent=(Math.Abs(x.Y*y.Z)+Math.Abs(x.Z*y.Y))*Math.Abs(z.X)+(Math.Abs(x.Z*y.X)+Math.Abs(x.X*y.Z))*Math.Abs(z.Y)+(Math.Abs(x.X*y.Y)+Math.Abs(x.Y*y.X))*Math.Abs(z.Z);
            // Hull inputs lie on the normalized binary lattice. If every
            // determinant product is zero, no cancellation is involved and
            // exact coplanarity needs no BigInteger fallback.
            if(permanent==0)return 0;
            if(Math.Abs(determinant)>permanent*1e-13)return Math.Sign(determinant);
            var values=new[]{a.X,a.Y,a.Z,b.X,b.Y,b.Z,c.X,c.Y,c.Z,d.X,d.Y,d.Z};var mantissas=new long[12];var exponents=new int[12];int minimum=int.MaxValue;
            for(int i=0;i<12;i++)
            {
                long bits=BitConverter.DoubleToInt64Bits(values[i]);long mantissa=bits&0x000fffffffffffffL;int exponent=(int)((bits>>52)&2047);
                if(exponent!=0){mantissa|=0x0010000000000000L;exponent-=1075;}else exponent=-1074;
                if(bits<0)mantissa=-mantissa;mantissas[i]=mantissa;exponents[i]=exponent;if(mantissa!=0)minimum=Math.Min(minimum,exponent);
            }
            if(minimum==int.MaxValue)return 0;
            var q=new BigInteger[12];for(int i=0;i<12;i++)q[i]=mantissas[i]==0?BigInteger.Zero:new BigInteger(mantissas[i])<<(exponents[i]-minimum);
            var bx=q[3]-q[0];var by=q[4]-q[1];var bz=q[5]-q[2];var cx=q[6]-q[0];var cy=q[7]-q[1];var cz=q[8]-q[2];var dx=q[9]-q[0];var dy=q[10]-q[1];var dz=q[11]-q[2];
            return ((by*cz-bz*cy)*dx+(bz*cx-bx*cz)*dy+(bx*cy-by*cx)*dz).Sign;
        }
        internal static (TriangleMesh Mesh,Plane3[] Planes) Build(Vec3[] points)
        {
            // All hull operations use normalized geometry. Binary-grid welding
            // bounds the displacement to 5.2e-8 and keeps cross products exact
            // enough for skinny boundary triangles; visibility still uses an
            // exact orientation sign. This is far below a policy cell.
            const double lattice=16777216;
            points=points.Select(p=>new Vec3(Math.Round(p.X*lattice)/lattice,Math.Round(p.Y*lattice)/lattice,Math.Round(p.Z*lattice)/lattice)).GroupBy(p=>(p.X,p.Y,p.Z)).Select(g=>g.First()).ToArray();
            int a=0;for(int i=1;i<points.Length;i++)if(points[i].X<points[a].X)a=i;
            int b=0;double best=0;for(int i=0;i<points.Length;i++){double d=(points[i]-points[a]).LengthSquared;if(d>best){best=d;b=i;}}
            if(best<1e-18)throw new ArgumentException("Mesh has no spatial extent.");
            int c=0;best=0;for(int i=0;i<points.Length;i++){double d=Vec3.Cross(points[b]-points[a],points[i]-points[a]).LengthSquared;if(d>best){best=d;c=i;}}
            if(best<1e-18)throw new ArgumentException("Collinear mesh cannot define a solid.");
            var plane=Plane3.Triangle(points[a],points[b],points[c]);int d0=0;best=0;for(int i=0;i<points.Length;i++){double d=Math.Abs(plane.At(points[i]));if(d>best){best=d;d0=i;}}
            if(best<1e-9)throw new ArgumentException("Planar mesh cannot define a solid.");
            var interior=(points[a]+points[b]+points[c]+points[d0])/4;
            var faces=new List<HullFace>{new HullFace(a,b,c,points,interior),new HullFace(a,d0,b,points,interior),new HullFace(b,d0,c,points,interior),new HullFace(c,d0,a,points,interior)};
            for(int i=0;i<points.Length;i++)
            {
                var visible=faces.Where(f=>Orientation(points[f.A],points[f.B],points[f.C],points[i])>0).ToArray();if(visible.Length==0)continue;
                var edges=new Dictionary<(int,int),(int,int)>();
                foreach(var f in visible)
                {foreach(var edge in new[]{(f.A,f.B),(f.B,f.C),(f.C,f.A)}){var key=edge.Item1<edge.Item2?edge:(edge.Item2,edge.Item1);if(edges.ContainsKey(key))edges.Remove(key);else edges.Add(key,edge);}faces.Remove(f);}
                foreach(var edge in edges.Values)faces.Add(new HullFace(edge.Item1,edge.Item2,i,points,interior));
            }
            var ids=faces.SelectMany(f=>new[]{f.A,f.B,f.C}).Distinct().ToArray();var map=new Dictionary<int,int>();for(int i=0;i<ids.Length;i++)map.Add(ids[i],i);
            var verts=ids.Select(i=>points[i]).ToArray();var triangles=faces.SelectMany(f=>new[]{map[f.A],map[f.B],map[f.C]}).ToArray();
            return(new TriangleMesh(verts,triangles),faces.Select(f=>f.Plane).ToArray());
        }
    }

    internal sealed class Polyhedron
    {
        private readonly List<List<Vec3>> _faces;
        internal Polyhedron(TriangleMesh hull){_faces=new List<List<Vec3>>();for(int i=0;i<hull.Indices.Length;i+=3)_faces.Add(new List<Vec3>{hull.Vertices[hull.Indices[i]],hull.Vertices[hull.Indices[i+1]],hull.Vertices[hull.Indices[i+2]]});}
        internal bool Clip(Plane3 plane)
        {
            var faces=new List<List<Vec3>>();var cap=new List<Vec3>();
            foreach(var old in _faces)
            {
                var polygon=new List<Vec3>();
                for(int i=0;i<old.Count;i++)
                {
                    Vec3 a=old[i],b=old[(i+1)%old.Count];double da=plane.At(a),db=plane.At(b);bool ina=da<=1e-9,inb=db<=1e-9;
                    if(ina)polygon.Add(a);
                    if(ina!=inb){var q=a+(b-a)*(da/(da-db));polygon.Add(q);if(!cap.Any(p=>(p-q).LengthSquared<1e-18))cap.Add(q);}
                }
                if(polygon.Count>=3)faces.Add(polygon);
            }
            if(cap.Count>=3)
            {
                Vec3 center=new Vec3();foreach(var p in cap)center+=p;center/=cap.Count;Vec3 axis=Math.Abs(plane.Normal.X)<.8?new Vec3(1,0,0):new Vec3(0,1,0);var u=Vec3.Cross(plane.Normal,axis).Normalized;var v=Vec3.Cross(plane.Normal,u);
                cap.Sort((a,b)=>Math.Atan2(Vec3.Dot(a-center,v),Vec3.Dot(a-center,u)).CompareTo(Math.Atan2(Vec3.Dot(b-center,v),Vec3.Dot(b-center,u))));faces.Add(cap);
            }
            _faces.Clear();_faces.AddRange(faces);return _faces.Count>0;
        }
        internal TriangleMesh ToMesh()
        {
            var vertices=new List<Vec3>();var map=new Dictionary<(long,long,long),int>();var indices=new List<int>();
            int Add(Vec3 p){var key=((long)Math.Round(p.X*1e8),(long)Math.Round(p.Y*1e8),(long)Math.Round(p.Z*1e8));if(map.TryGetValue(key,out int i))return i;i=vertices.Count;vertices.Add(p);map.Add(key,i);return i;}
            foreach(var f in _faces)for(int k=1;k+1<f.Count;k++)if(Vec3.Cross(f[k]-f[0],f[k+1]-f[0]).LengthSquared>1e-20){int a=Add(f[0]),b=Add(f[k]),c=Add(f[k+1]);if(a!=b&&b!=c&&a!=c){indices.Add(a);indices.Add(b);indices.Add(c);}}
            return new TriangleMesh(vertices.ToArray(),indices.ToArray());
        }
    }
}
