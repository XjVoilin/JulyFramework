#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace July.Physics.Authoring.PickingProxy
{
    internal static class Numerics
    {
        internal static Vec3[] Pca(IReadOnlyList<Vec3> points)
        {
            var center=new Vec3();foreach(var p in points)center+=p;center/=points.Count;var a=new double[3,3];var v=new double[3,3];for(int i=0;i<3;i++)v[i,i]=1;
            foreach(var p in points){var d=p-center;for(int i=0;i<3;i++)for(int j=0;j<3;j++)a[i,j]+=d[i]*d[j];}
            for(int iteration=0;iteration<40;iteration++)
            {
                int p=0,q=1;double best=Math.Abs(a[0,1]);if(Math.Abs(a[0,2])>best){p=0;q=2;best=Math.Abs(a[0,2]);}if(Math.Abs(a[1,2])>best){p=1;q=2;best=Math.Abs(a[1,2]);}if(best<1e-14)break;
                double angle=.5*Math.Atan2(2*a[p,q],a[q,q]-a[p,p]),c=Math.Cos(angle),s=Math.Sin(angle);
                for(int k=0;k<3;k++){double x=a[k,p],y=a[k,q];a[k,p]=c*x-s*y;a[k,q]=s*x+c*y;}
                for(int k=0;k<3;k++){double x=a[p,k],y=a[q,k];a[p,k]=c*x-s*y;a[q,k]=s*x+c*y;}
                for(int k=0;k<3;k++){double x=v[k,p],y=v[k,q];v[k,p]=c*x-s*y;v[k,q]=s*x+c*y;}
            }
            var ids=Enumerable.Range(0,3).OrderByDescending(i=>a[i,i]).ToArray();var axes=ids.Select(i=>new Vec3(v[0,i],v[1,i],v[2,i]).Normalized).ToArray();axes[2]=Vec3.Cross(axes[0],axes[1]).Normalized;return axes;
        }
        internal static Vec3[] SphereDirections(int count)
        {var result=new Vec3[count];for(int i=0;i<count;i++){double z=1-2*(i+.5)/count,angle=i*Math.PI*(3-Math.Sqrt(5)),r=Math.Sqrt(1-z*z);result[i]=new Vec3(r*Math.Cos(angle),r*Math.Sin(angle),z);}return result;}
        internal static Vec3[] PlaneTemplate()
        {var n=new List<Vec3>();for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)if(x!=0||y!=0||z!=0)n.Add(new Vec3(x,y,z).Normalized);return n.ToArray();}
        internal static void ProjectionBasis(Vec3 normal,out Vec3 x,out Vec3 y)
        {var a=Math.Abs(normal.X)<=Math.Abs(normal.Y)&&Math.Abs(normal.X)<=Math.Abs(normal.Z)?new Vec3(1,0,0):Math.Abs(normal.Y)<=Math.Abs(normal.Z)?new Vec3(0,1,0):new Vec3(0,0,1);x=Vec3.Cross(normal,a).Normalized;y=Vec3.Cross(normal,x);}
        internal static bool SegmentInterval(Vec3 start,Vec3 direction,double length,Plane3[] planes,out double lo,out double hi)
        {
            lo=0;hi=length;foreach(var plane in planes){double b=plane.At(start),d=Vec3.Dot(plane.Normal,direction);if(Math.Abs(d)<1e-12){if(b>1e-9)return false;continue;}double t=-b/d;if(d<0)lo=Math.Max(lo,t);else hi=Math.Min(hi,t);if(lo>hi)return false;}return hi>lo;
        }
        internal static int[] Hull2D(Vec3[] p)
        {
            var order=Enumerable.Range(0,p.Length).OrderBy(i=>p[i].X).ThenBy(i=>p[i].Y).ToArray();var lower=new List<int>();double Cross(int a,int b,int c)=>(p[b].X-p[a].X)*(p[c].Y-p[a].Y)-(p[b].Y-p[a].Y)*(p[c].X-p[a].X);
            foreach(int i in order){while(lower.Count>=2&&Cross(lower[lower.Count-2],lower[lower.Count-1],i)<=1e-12)lower.RemoveAt(lower.Count-1);lower.Add(i);}int n=lower.Count;
            for(int k=order.Length-2;k>=0;k--){int i=order[k];while(lower.Count>n&&Cross(lower[lower.Count-2],lower[lower.Count-1],i)<=1e-12)lower.RemoveAt(lower.Count-1);lower.Add(i);}if(lower.Count>1)lower.RemoveAt(lower.Count-1);return lower.ToArray();
        }
    }

    internal sealed class Grid3
    {
        internal readonly int X,Y,Z,Count;
        internal readonly Vec3 Origin;
        internal readonly double Cell;
        internal Grid3(int x,int y,int z,Vec3 origin,double cell){X=x;Y=y;Z=z;Count=x*y*z;Origin=origin;Cell=cell;}
        internal int Index(int x,int y,int z)=>(x*Y+y)*Z+z;
        internal Vec3 Point(int index){int z=index%Z,y=index/Z%Y,x=index/(Y*Z);return Origin+new Vec3(x+.5,y+.5,z+.5)*Cell;}
        internal int At(Vec3 p){var d=(p-Origin)/Cell;int x=(int)Math.Floor(d.X),y=(int)Math.Floor(d.Y),z=(int)Math.Floor(d.Z);return x<0||y<0||z<0||x>=X||y>=Y||z>=Z?-1:Index(x,y,z);}
        internal IEnumerable<int> Neighbors(int i)
        {int z=i%Z,y=i/Z%Y,x=i/(Y*Z);if(x>0)yield return i-Y*Z;if(x+1<X)yield return i+Y*Z;if(y>0)yield return i-Z;if(y+1<Y)yield return i+Z;if(z>0)yield return i-1;if(z+1<Z)yield return i+1;}
        internal double[] DistanceToFalse(bool[] mask)
        {
            var d=new double[Count];for(int i=0;i<Count;i++)d[i]=mask[i]?double.PositiveInfinity:0;int longest=Math.Max(X,Math.Max(Y,Z));var f=new double[longest];var g=new double[longest];var v=new int[longest];var cut=new double[longest+1];
            for(int y=0;y<Y;y++)for(int z=0;z<Z;z++){for(int x=0;x<X;x++)f[x]=d[Index(x,y,z)];Transform(f,g,X,v,cut);for(int x=0;x<X;x++)d[Index(x,y,z)]=g[x];}
            for(int x=0;x<X;x++)for(int z=0;z<Z;z++){for(int y=0;y<Y;y++)f[y]=d[Index(x,y,z)];Transform(f,g,Y,v,cut);for(int y=0;y<Y;y++)d[Index(x,y,z)]=g[y];}
            for(int x=0;x<X;x++)for(int y=0;y<Y;y++){for(int z=0;z<Z;z++)f[z]=d[Index(x,y,z)];Transform(f,g,Z,v,cut);for(int z=0;z<Z;z++)d[Index(x,y,z)]=Math.Sqrt(g[z]);}return d;
        }
        private static void Transform(double[] f,double[] result,int count,int[] v,double[] cut)
        {
            int k=-1;for(int q=0;q<count;q++)
            {
                if(double.IsInfinity(f[q]))continue;double s=double.NegativeInfinity;
                while(k>=0){int p=v[k];s=((f[q]+q*(double)q)-(f[p]+p*(double)p))/(2*(q-p));if(s>cut[k])break;k--;}
                k++;v[k]=q;cut[k]=k==0?double.NegativeInfinity:s;cut[k+1]=double.PositiveInfinity;
            }
            if(k<0){for(int q=0;q<count;q++)result[q]=double.PositiveInfinity;return;}
            int j=0;for(int q=0;q<count;q++){while(j<k&&cut[j+1]<q)j++;double t=q-v[j];result[q]=t*t+f[v[j]];}
        }
    }
}
