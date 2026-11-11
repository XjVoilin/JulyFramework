#nullable enable
using System;
using System.Collections.Generic;

namespace July.Physics.Authoring.PickingProxy
{
    internal sealed class MeshQueries
    {
        private readonly TriangleMesh _mesh;
        private readonly int[] _triangles;
        private readonly List<Node> _nodes=new List<Node>();
        private readonly struct Node
        {
            internal readonly Vec3 Min,Max;
            internal readonly int Start,Count,Left,Right;
            internal Node(Vec3 min,Vec3 max,int start,int count,int left,int right){Min=min;Max=max;Start=start;Count=count;Left=left;Right=right;}
        }
        internal MeshQueries(TriangleMesh mesh)
        {
            _mesh=mesh;var triangles=new List<int>();for(int i=0;i<mesh.Indices.Length;i+=3)if(Vec3.Cross(mesh.Vertices[mesh.Indices[i+1]]-mesh.Vertices[mesh.Indices[i]],mesh.Vertices[mesh.Indices[i+2]]-mesh.Vertices[mesh.Indices[i]]).LengthSquared>1e-24)triangles.Add(i);
            if(triangles.Count==0)throw new ArgumentException("Mesh contains no usable triangles.");_triangles=triangles.ToArray();Build(0,_triangles.Length);
        }
        private int Build(int start,int count)
        {
            var min=new Vec3(double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity);var max=-min;
            for(int i=start;i<start+count;i++)for(int k=0;k<3;k++){var p=_mesh.Vertices[_mesh.Indices[_triangles[i]+k]];min=Vec3.Min(min,p);max=Vec3.Max(max,p);}
            int index=_nodes.Count;_nodes.Add(default);
            if(count<=8){_nodes[index]=new Node(min,max,start,count,-1,-1);return index;}
            var extent=max-min;int axis=extent.X>=extent.Y&&extent.X>=extent.Z?0:extent.Y>=extent.Z?1:2;
            Array.Sort(_triangles,start,count,Comparer<int>.Create((a,b)=>Centroid(a)[axis].CompareTo(Centroid(b)[axis])));
            int left=Build(start,count/2),right=Build(start+count/2,count-count/2);_nodes[index]=new Node(min,max,0,0,left,right);return index;
        }
        private Vec3 Centroid(int t)=>(_mesh.Vertices[_mesh.Indices[t]]+_mesh.Vertices[_mesh.Indices[t+1]]+_mesh.Vertices[_mesh.Indices[t+2]])/3;
        internal void GpuArrays(out float[] nodes,out float[] triangles)
        {
            nodes=new float[_nodes.Count*12];triangles=new float[_triangles.Length*12];
            for(int i=0;i<_nodes.Count;i++){var n=_nodes[i];for(int a=0;a<3;a++){nodes[i*12+a]=(float)n.Min[a];nodes[i*12+4+a]=(float)n.Max[a];}nodes[i*12+3]=n.Left;nodes[i*12+7]=n.Right;nodes[i*12+8]=n.Start;nodes[i*12+9]=n.Count;}
            for(int i=0;i<_triangles.Length;i++){int t=_triangles[i];var a=_mesh.Vertices[_mesh.Indices[t]];var u=_mesh.Vertices[_mesh.Indices[t+1]]-a;var v=_mesh.Vertices[_mesh.Indices[t+2]]-a;for(int axis=0;axis<3;axis++){triangles[i*12+axis]=(float)a[axis];triangles[i*12+4+axis]=(float)u[axis];triangles[i*12+8+axis]=(float)v[axis];}triangles[i*12+3]=(float)Vec3.Cross(u,v).Length;}
        }
        private static bool RayBox(Vec3 start,Vec3 direction,Vec3 min,Vec3 max,double distance)
        {
            double lo=0,hi=distance;for(int axis=0;axis<3;axis++)
            {double d=direction[axis];if(Math.Abs(d)<1e-14){if(start[axis]<min[axis]||start[axis]>max[axis])return false;continue;}double a=(min[axis]-start[axis])/d,b=(max[axis]-start[axis])/d;if(a>b){double t=a;a=b;b=t;}lo=Math.Max(lo,a);hi=Math.Min(hi,b);if(lo>hi)return false;}
            return true;
        }
        internal double Ray(Vec3 start,Vec3 direction,double maxDistance=double.PositiveInfinity)
        {
            Span<int> stack=stackalloc int[64];int count=1;stack[0]=0;double nearest=maxDistance;bool hit=false;
            while(count>0)
            {
                var node=_nodes[stack[--count]];if(!RayBox(start,direction,node.Min,node.Max,nearest))continue;
                if(node.Count==0){stack[count++]=node.Left;stack[count++]=node.Right;continue;}
                for(int i=node.Start;i<node.Start+node.Count;i++)
                {
                    int t=_triangles[i];var a=_mesh.Vertices[_mesh.Indices[t]];var u=_mesh.Vertices[_mesh.Indices[t+1]]-a;var v=_mesh.Vertices[_mesh.Indices[t+2]]-a;var p=Vec3.Cross(direction,v);double det=Vec3.Dot(u,p);if(Math.Abs(det)<1e-14)continue;
                    var s=start-a;double b=Vec3.Dot(s,p)/det;if(b< -1e-10||b>1+1e-10)continue;var q=Vec3.Cross(s,u);double c=Vec3.Dot(direction,q)/det;if(c< -1e-10||b+c>1+1e-10)continue;double d=Vec3.Dot(v,q)/det;
                    if(d>1e-8&&d<nearest){nearest=d;hit=true;}
                }
            }
            return hit?nearest:double.PositiveInfinity;
        }
        internal bool Occluded(Vec3 start,Vec3 direction,double maxDistance=double.PositiveInfinity)
        {
            Span<int> stack=stackalloc int[64];int count=1;stack[0]=0;
            while(count>0)
            {
                var node=_nodes[stack[--count]];if(!RayBox(start,direction,node.Min,node.Max,maxDistance))continue;
                if(node.Count==0){stack[count++]=node.Left;stack[count++]=node.Right;continue;}
                for(int i=node.Start;i<node.Start+node.Count;i++)
                {
                    int t=_triangles[i];var a=_mesh.Vertices[_mesh.Indices[t]];var u=_mesh.Vertices[_mesh.Indices[t+1]]-a;var v=_mesh.Vertices[_mesh.Indices[t+2]]-a;var p=Vec3.Cross(direction,v);double det=Vec3.Dot(u,p);if(Math.Abs(det)<1e-14)continue;
                    var s=start-a;double b=Vec3.Dot(s,p)/det;if(b< -1e-10||b>1+1e-10)continue;var q=Vec3.Cross(s,u);double c=Vec3.Dot(direction,q)/det;if(c< -1e-10||b+c>1+1e-10)continue;double d=Vec3.Dot(v,q)/det;
                    if(d>1e-8&&d<maxDistance)return true;
                }
            }
            return false;
        }
        internal bool Inside(Vec3 point)
        {
            int inside=0;foreach(var raw in new[]{new Vec3(1,.371,.217),new Vec3(.193,1,.467),new Vec3(.417,.293,1),new Vec3(-1,.238,.713),new Vec3(.621,-1,.327)})
            {var direction=raw.Normalized;var origin=point;int hits=0;while(true){double t=Ray(origin,direction);if(double.IsInfinity(t))break;hits++;origin+=(t+1e-7)*direction;}if((hits&1)!=0)inside++;}
            return inside>=3;
        }
        private static double BoxDistance(Vec3 p,Vec3 min,Vec3 max)
        {double s=0;for(int i=0;i<3;i++){double d=Math.Max(0,Math.Max(min[i]-p[i],p[i]-max[i]));s+=d*d;}return s;}
        internal double Distance(Vec3 point)
        {
            Span<int> stack=stackalloc int[64];int count=1;stack[0]=0;double best=double.PositiveInfinity;
            while(count>0)
            {
                var node=_nodes[stack[--count]];if(BoxDistance(point,node.Min,node.Max)>best)continue;
                if(node.Count==0){stack[count++]=node.Left;stack[count++]=node.Right;continue;}
                for(int i=node.Start;i<node.Start+node.Count;i++){int t=_triangles[i];double d=TriangleDistanceSquared(point,_mesh.Vertices[_mesh.Indices[t]],_mesh.Vertices[_mesh.Indices[t+1]],_mesh.Vertices[_mesh.Indices[t+2]]);if(d<best)best=d;}
            }
            return Math.Sqrt(best);
        }
        private static double TriangleDistanceSquared(Vec3 p,Vec3 a,Vec3 b,Vec3 c)
        {
            var ab=b-a;var ac=c-a;var ap=p-a;double d1=Vec3.Dot(ab,ap),d2=Vec3.Dot(ac,ap);if(d1<=0&&d2<=0)return ap.LengthSquared;
            var bp=p-b;double d3=Vec3.Dot(ab,bp),d4=Vec3.Dot(ac,bp);if(d3>=0&&d4<=d3)return bp.LengthSquared;
            double vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return(p-(a+ab*(d1/(d1-d3)))).LengthSquared;
            var cp=p-c;double d5=Vec3.Dot(ab,cp),d6=Vec3.Dot(ac,cp);if(d6>=0&&d5<=d6)return cp.LengthSquared;
            double vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return(p-(a+ac*(d2/(d2-d6)))).LengthSquared;
            double va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return(p-(b+(c-b)*((d4-d3)/(d4-d3+d5-d6)))).LengthSquared;
            double sum=1/(va+vb+vc);return(p-(a+ab*(vb*sum)+ac*(vc*sum))).LengthSquared;
        }
    }
}
