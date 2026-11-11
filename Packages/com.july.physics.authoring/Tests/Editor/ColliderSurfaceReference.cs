using System;
using System.Linq;
using UnityEngine;

namespace July.Physics.Authoring
{
    // Double-precision source geometry reference, independent of PhysX cooking.
    internal static class ColliderSurfaceReference
    {
        internal static Vector3 ClosestSurface(Vector3 point, Vector3[] vertices, int[] indices)
        {
            var closest = vertices[0]; float best = float.PositiveInfinity;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var candidate = ClosestTriangle(point, vertices[indices[i]], vertices[indices[i+1]], vertices[indices[i+2]]);
                float distance = (candidate - point).sqrMagnitude;
                if (distance < best) { best = distance; closest = candidate; }
            }
            return closest;
        }

        static Vector3 ClosestSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var d = b-a; float length = d.sqrMagnitude;
            return length == 0 ? a : a + d * Mathf.Clamp01(Vector3.Dot(p-a,d)/length);
        }

        static Vector3 ClosestTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
            => ClosestTriangleDouble(new D(p), new D(a), new D(b), new D(c)).Vector;

        struct D
        {
            public double x,y,z;
            public D(Vector3 v) { x=v.x; y=v.y; z=v.z; }
            D(double x,double y,double z) { this.x=x;this.y=y;this.z=z; }
            public Vector3 Vector => new Vector3((float)x,(float)y,(float)z);
            public static D operator +(D a,D b)=>new D(a.x+b.x,a.y+b.y,a.z+b.z);
            public static D operator -(D a,D b)=>new D(a.x-b.x,a.y-b.y,a.z-b.z);
            public static D operator *(D a,double b)=>new D(a.x*b,a.y*b,a.z*b);
            public static double Dot(D a,D b)=>a.x*b.x+a.y*b.y+a.z*b.z;
            public static D Cross(D a,D b)=>new D(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
        }

        internal static bool Raycast(Ray ray,Vector3[] v,int[] ids,double maximum,out double hit)
        {
            double near=0,far=maximum; var origin=new D(ray.origin);var direction=new D(ray.direction);
            for(int i=0;i<ids.Length;i+=3)
            {
                var a=new D(v[ids[i]]); var normal=D.Cross(new D(v[ids[i+1]])-a,new D(v[ids[i+2]])-a);
                double length=Math.Sqrt(D.Dot(normal,normal)); if(length<1e-20)continue;
                if(D.Dot(normal,a)<0) normal=normal*(-1);
                double distance=D.Dot(normal,origin-a)/length, slope=D.Dot(normal,direction)/length;
                if(Math.Abs(slope)<1e-14) {if(distance>1e-9){hit=0;return false;}continue;}
                double t=-distance/slope;
                if(slope<0)near=Math.Max(near,t);else far=Math.Min(far,t);
            }
            hit=near; return near<=far && near<=maximum && far>=0;
        }

        static D ClosestTriangleDouble(D p,D a,D b,D c)
        {
            var ab=b-a; var ac=c-a;
            var cross=D.Cross(ab,ac);
            if (D.Dot(cross,cross) < 1e-30)
                return new D(new[] { ClosestSegment(p.Vector,a.Vector,b.Vector), ClosestSegment(p.Vector,b.Vector,c.Vector), ClosestSegment(p.Vector,c.Vector,a.Vector) }.OrderBy(v => (v-p.Vector).sqrMagnitude).First());
            var ap=p-a; double d1=D.Dot(ab,ap), d2=D.Dot(ac,ap);
            if (d1<=0 && d2<=0) return a;
            var bp=p-b; double d3=D.Dot(ab,bp), d4=D.Dot(ac,bp);
            if (d3>=0 && d4<=d3) return b;
            double vc=d1*d4-d3*d2;
            if (vc<=0 && d1>=0 && d3<=0) return a+ab*(d1/(d1-d3));
            var cp=p-c; double d5=D.Dot(ab,cp), d6=D.Dot(ac,cp);
            if (d6>=0 && d5<=d6) return c;
            double vb=d5*d2-d1*d6;
            if (vb<=0 && d2>=0 && d6<=0) return a+ac*(d2/(d2-d6));
            double va=d3*d6-d5*d4;
            if (va<=0 && d4-d3>=0 && d5-d6>=0) return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
            double inv=1/(va+vb+vc);
            return a+ab*(vb*inv)+ac*(vc*inv);
        }
    }
}
