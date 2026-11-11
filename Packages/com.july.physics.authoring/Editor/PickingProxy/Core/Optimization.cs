#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace July.Physics.Authoring.PickingProxy
{
    internal sealed class ConvexModel
    {
        internal readonly int Count,Planes;
        internal readonly float[] Centers,Normals,Radii,InitialNormals;
        internal ConvexModel(int count,int planes,float[] centers,float[] normals,float[] radii)
        {Count=count;Planes=planes;Centers=centers;Normals=normals;Radii=radii;InitialNormals=(float[])normals.Clone();}
        internal static ConvexModel Initialize(Vec3[] positive,int count,int seed)
        {
            var random=new Random(seed);int n=Math.Min(18000,positive.Length);var sample=new Vec3[n];for(int i=0;i<n;i++)sample[i]=positive[random.Next(positive.Length)];var centers=new Vec3[count];centers[0]=sample[random.Next(n)];var distance=Enumerable.Repeat(double.PositiveInfinity,n).ToArray();
            for(int k=1;k<count;k++){double sum=0;for(int i=0;i<n;i++){distance[i]=Math.Min(distance[i],(sample[i]-centers[k-1]).LengthSquared);sum+=distance[i];}double t=random.NextDouble()*sum;int chosen=n-1;for(int i=0;i<n;i++){t-=distance[i];if(t<=0){chosen=i;break;}}centers[k]=sample[chosen];}
            var labels=new int[n];for(int iteration=0;iteration<40;iteration++)
            {var sums=new Vec3[count];var counts=new int[count];for(int i=0;i<n;i++){int best=0;double d=double.PositiveInfinity;for(int k=0;k<count;k++){double q=(sample[i]-centers[k]).LengthSquared;if(q<d){d=q;best=k;}}labels[i]=best;sums[best]+=sample[i];counts[best]++;}for(int k=0;k<count;k++)if(counts[k]>0)centers[k]=sums[k]/counts[k];else centers[k]=sample[random.Next(n)];}
            var template=Numerics.PlaneTemplate();var c=new float[count*3];var normals=new float[count*template.Length*3];var radii=new float[count*template.Length];
            for(int k=0;k<count;k++)
            {var cluster=Enumerable.Range(0,n).Where(i=>labels[i]==k).Select(i=>sample[i]).ToArray();if(cluster.Length<4)throw new InvalidOperationException("Initial convex cluster has too few points.");var basis=Numerics.Pca(cluster);c[k*3]=(float)centers[k].X;c[k*3+1]=(float)centers[k].Y;c[k*3+2]=(float)centers[k].Z;
                for(int j=0;j<template.Length;j++){var u=template[j];var normal=u.X*basis[0]+u.Y*basis[1]+u.Z*basis[2];int p=k*template.Length+j;normals[p*3]=(float)normal.X;normals[p*3+1]=(float)normal.Y;normals[p*3+2]=(float)normal.Z;radii[p]=(float)Math.Max(.003,cluster.Max(q=>Vec3.Dot(q-centers[k],normal))+.002);}}
            return new ConvexModel(count,template.Length,c,normals,radii);
        }
    }
    internal readonly struct TrainingRay
    {
        internal readonly Vec3 Start,End;
        internal readonly double Radius,Length,HEnter,HLeave;
        internal readonly bool HParallelOutside;
        internal TrainingRay(Vec3 start,Vec3 end,double radius,Plane3[] hull)
        {
            Start=start;End=end;Radius=radius;var delta=end-start;Length=delta.Length;double lo=0,hi=1;bool outside=false;
            foreach(var plane in hull){double a=plane.At(start)-radius,b=plane.At(end)-radius,d=b-a;if(Math.Abs(d)<1e-7){if(a>0)outside=true;continue;}double t=-a/d;if(d<0)lo=Math.Max(lo,t);else hi=Math.Min(hi,t);}HEnter=lo;HLeave=hi;HParallelOutside=outside;
        }
    }
    internal sealed class TrainingData
    {
        internal Vec3[] Body=Array.Empty<Vec3>(),Fill=Array.Empty<Vec3>(),Negative=Array.Empty<Vec3>();
        internal TrainingRay[] Rays=Array.Empty<TrainingRay>();
        internal Plane3[] HullPlanes=Array.Empty<Plane3>();
        internal double Cell;
    }
    internal sealed class ModelGradient
    {
        internal readonly float[] Centers,Normals,Radii;
        internal double PositiveLoss,NegativeLoss,RayLoss,Regularization;
        internal double TotalLoss=>PositiveLoss+4*NegativeLoss+.5*RayLoss+Regularization;
        internal ModelGradient(ConvexModel model){Centers=new float[model.Centers.Length];Normals=new float[model.Normals.Length];Radii=new float[model.Radii.Length];}
    }
    internal sealed class Objective
    {
        private readonly ConvexModel _model;
        private readonly float[] _normals,_offsets,_lengths;
        private readonly int _workers;
        internal Objective(ConvexModel model,int workers)
        {_model=model;_workers=workers;_normals=new float[model.Normals.Length];_offsets=new float[model.Radii.Length];_lengths=new float[model.Radii.Length];Refresh();}
        internal void Refresh()
        {
            for(int p=0;p<_model.Radii.Length;p++)
            {int i=p*3,k=p/_model.Planes,c=k*3;double length=Math.Sqrt(_model.Normals[i]*_model.Normals[i]+_model.Normals[i+1]*_model.Normals[i+1]+_model.Normals[i+2]*_model.Normals[i+2]);length=Math.Max(1e-12,length);_lengths[p]=(float)length;for(int axis=0;axis<3;axis++)_normals[i+axis]=(float)(_model.Normals[i+axis]/length);_offsets[p]=-_normals[i]*_model.Centers[c]-_normals[i+1]*_model.Centers[c+1]-_normals[i+2]*_model.Centers[c+2]-_model.Radii[p];}
        }
        private double Distance(Vec3 point,int unit,out int face,double cutoff=double.PositiveInfinity)
        {
            int first=unit*_model.Planes;face=first;double best=double.NegativeInfinity;
            for(int p=first;p<first+_model.Planes;p++){int i=3*p;double d=point.X*_normals[i]+point.Y*_normals[i+1]+point.Z*_normals[i+2]+_offsets[p];if(d>best){best=d;face=p;}if(best>=cutoff)break;}return best;
        }
        internal double[] PointScores(Vec3[] points,bool negative,CancellationToken token)
        {
            var result=new double[points.Length];Parallel.For(0,points.Length,new ParallelOptions{MaxDegreeOfParallelism=_workers,CancellationToken=token},i=>{double best=double.PositiveInfinity;for(int k=0;k<_model.Count;k++)best=Math.Min(best,Distance(points[i],k,out _,best));result[i]=negative?-best:best;});return result;
        }
        private double RayOverlap(TrainingRay ray,int unit,out int entryFace,out int exitFace,out double entry,out double exit,double rejectBelow=double.NegativeInfinity)
        {
            entryFace=-1;exitFace=-1;entry=Math.Max(0,ray.HEnter);exit=Math.Min(1,ray.HLeave);if(ray.HParallelOutside)return -1;int first=unit*_model.Planes;var delta=ray.End-ray.Start;
            for(int p=first;p<first+_model.Planes;p++)
            {int i=p*3;double a=ray.Start.X*_normals[i]+ray.Start.Y*_normals[i+1]+ray.Start.Z*_normals[i+2]+_offsets[p]-ray.Radius;double d=delta.X*_normals[i]+delta.Y*_normals[i+1]+delta.Z*_normals[i+2];if(Math.Abs(d)<1e-7){if(a>0)return -1;continue;}double t=-a/d;if(d<0&&t>entry){entry=t;entryFace=p;}else if(d>0&&t<exit){exit=t;exitFace=p;}if((exit-entry)*ray.Length<rejectBelow)return rejectBelow;}
            return(exit-entry)*ray.Length;
        }
        internal double[] RayScores(TrainingRay[] rays,CancellationToken token)
        {
            var result=new double[rays.Length];Parallel.For(0,rays.Length,new ParallelOptions{MaxDegreeOfParallelism=_workers,CancellationToken=token},i=>{double worst=-1;for(int k=0;k<_model.Count;k++)worst=Math.Max(worst,RayOverlap(rays[i],k,out _,out _,out _,out _,-1));result[i]=worst;});return result;
        }
        private void PointGradient(ModelGradient gradient,Vec3 point,int face,double weight)
        {
            int n=face*3,c=face/_model.Planes*3;gradient.Radii[face]-=(float)weight;
            for(int a=0;a<3;a++){gradient.Centers[c+a]-=(float)(weight*_normals[n+a]);gradient.Normals[n+a]+=(float)(weight*(point[a]-_model.Centers[c+a]));}
        }
        private void Positive(Vec3[] points,double slack,double cell,ModelGradient gradient)
        {
            if(points.Length==0)return;var errors=new double[points.Length];var faces=new int[points.Length];
            Parallel.For(0,points.Length,new ParallelOptions{MaxDegreeOfParallelism=_workers},i=>{double best=double.PositiveInfinity;int chosen=0;for(int k=0;k<_model.Count;k++){double d=Distance(points[i],k,out int face,best);if(d<best){best=d;chosen=face;}}errors[i]=Math.Max(0,(best-slack*cell)/cell);faces[i]=chosen;});
            int top=Math.Min(80,points.Length);var ids=Enumerable.Range(0,points.Length).OrderByDescending(i=>errors[i]).Take(top).ToArray();var weights=Enumerable.Repeat(1.0/points.Length,points.Length).ToArray();foreach(int i in ids)weights[i]+=.5/top;
            for(int i=0;i<points.Length;i++){gradient.PositiveLoss+=weights[i]*errors[i]*errors[i];if(errors[i]>0)PointGradient(gradient,points[i],faces[i],2*weights[i]*errors[i]/cell);}
        }
        private void RayGradient(ModelGradient gradient,TrainingRay ray,int face,double weight)
        {
            if(face<0)return;int n=face*3,c=face/_model.Planes*3;var delta=ray.End-ray.Start;double a=ray.Start.X*_normals[n]+ray.Start.Y*_normals[n+1]+ray.Start.Z*_normals[n+2]+_offsets[face]-ray.Radius;double den=delta.X*_normals[n]+delta.Y*_normals[n+1]+delta.Z*_normals[n+2];
            gradient.Radii[face]+=(float)(weight/den);
            for(int axis=0;axis<3;axis++){gradient.Centers[c+axis]+=(float)(weight*_normals[n+axis]/den);gradient.Normals[n+axis]+=(float)(-weight*((ray.Start[axis]-_model.Centers[c+axis])*den-a*delta[axis])/(den*den));}
        }
        internal ModelGradient Evaluate(Vec3[] body,Vec3[] fill,Vec3[] negative,TrainingRay[] rays,double cell,double bodySlack=.6,double fillSlack=.2)
        {
            Refresh();var gradient=new ModelGradient(_model);Positive(body,bodySlack,cell,gradient);Positive(fill,fillSlack,cell,gradient);var negatives=new double[_model.Count];var rayLosses=new double[_model.Count];
            Parallel.For(0,_model.Count,new ParallelOptions{MaxDegreeOfParallelism=_workers},k=>
            {
                foreach(var p in negative){double distance=Distance(p,k,out int face,.15*cell),error=Math.Max(0,(.15*cell-distance)/cell);negatives[k]+=negative.Length==0?0:error*error/negative.Length;if(error>0)PointGradient(gradient,p,face,-8*error/(cell*negative.Length));}
                foreach(var ray in rays)
                {double overlap=RayOverlap(ray,k,out int enterFace,out int leaveFace,out _,out _,-.025*cell),error=Math.Max(0,(overlap+.025*cell)/cell);rayLosses[k]+=rays.Length==0?0:error*error/rays.Length;if(error<=0)continue;double weight=error*ray.Length/(cell*rays.Length);RayGradient(gradient,ray,leaveFace,weight);RayGradient(gradient,ray,enterFace,-weight);}
            });
            gradient.NegativeLoss=negatives.Sum();gradient.RayLoss=rayLosses.Sum();double regularization=0;
            for(int p=0;p<_model.Radii.Length;p++)
            {
                int i=p*3;for(int a=0;a<3;a++){double d=_normals[i+a]-_model.InitialNormals[i+a];regularization+=.002*d*d/_normals.Length;gradient.Normals[i+a]+=(float)(.004*d/_normals.Length);}
                double dot=0;for(int a=0;a<3;a++)dot+=gradient.Normals[i+a]*_normals[i+a];for(int a=0;a<3;a++)gradient.Normals[i+a]=(float)((gradient.Normals[i+a]-_normals[i+a]*dot)/_lengths[p]);
            }
            gradient.Regularization=regularization;return gradient;
        }
    }
    internal static class Optimizer
    {
        private sealed class Adam
        {
            private readonly float[] _m,_v;
            internal Adam(int count){_m=new float[count];_v=new float[count];}
            internal void Step(float[] values,float[] gradient,double rate,int step,double scale)
            {double b1=1-Math.Pow(.9,step+1),b2=1-Math.Pow(.999,step+1);for(int i=0;i<values.Length;i++){double g=gradient[i]*scale;_m[i]=(float)(.9*_m[i]+.1*g);_v[i]=(float)(.999*_v[i]+.001*g*g);values[i]-=(float)(rate*(_m[i]/b1)/(Math.Sqrt(_v[i]/b2)+1e-8));}}
        }
        private static int[] Hardest(double[] scores)=>Enumerable.Range(0,scores.Length).OrderByDescending(i=>scores[i]).Take(Math.Min(4096,scores.Length)).ToArray();
        private static T[] Batch<T>(T[] data,int[] hard,T[] result,Random random)
        {if(data.Length==0)return Array.Empty<T>();int count=result.Length;for(int i=0;i<count;i++)result[i]=data[i<count/2||hard.Length==0?random.Next(data.Length):hard[random.Next(hard.Length)]];return result;}
        internal static ConvexModel Fit(TrainingData data,GenerationSettings settings,CancellationToken token,Action<string>? progress,ConvexModel? initial=null)
        {
            var model=initial??ConvexModel.Initialize(data.Body.Concat(data.Fill).ToArray(),settings.MaxConvexes,settings.Seed);var objective=new Objective(model,settings.Workers);var random=new Random(settings.Seed);var centerAdam=new Adam(model.Centers.Length);var normalAdam=new Adam(model.Normals.Length);var radiusAdam=new Adam(model.Radii.Length);int[] hb=Array.Empty<int>(),hf=Array.Empty<int>(),hn=Array.Empty<int>(),hr=Array.Empty<int>();
            var bodyBatch=new Vec3[768];var fillBatch=new Vec3[768];var negativeBatch=new Vec3[1536];var rayBatch=new TrainingRay[512];
            for(int step=0;step<settings.FitSteps;step++)
            {
                token.ThrowIfCancellationRequested();if(step%400==0){objective.Refresh();hb=Hardest(objective.PointScores(data.Body,false,token));hf=Hardest(objective.PointScores(data.Fill,false,token));hn=Hardest(objective.PointScores(data.Negative,true,token));hr=Hardest(objective.RayScores(data.Rays,token));}
                var gradient=objective.Evaluate(Batch(data.Body,hb,bodyBatch,random),Batch(data.Fill,hf,fillBatch,random),Batch(data.Negative,hn,negativeBatch,random),Batch(data.Rays,hr,rayBatch,random),data.Cell);double norm=0;foreach(var g in gradient.Centers)norm+=g*g;foreach(var g in gradient.Normals)norm+=g*g;foreach(var g in gradient.Radii)norm+=g*g;double scale=Math.Min(1,20/(Math.Sqrt(norm)+1e-6));double factor=.05+.95*.5*(1+Math.Cos(Math.PI*step/settings.FitSteps));
                centerAdam.Step(model.Centers,gradient.Centers,.0008*factor,step,scale);normalAdam.Step(model.Normals,gradient.Normals,.003*factor,step,scale);radiusAdam.Step(model.Radii,gradient.Radii,.0006*factor,step,scale);for(int p=0;p<model.Radii.Length;p++)model.Radii[p]=Math.Max(.0005f,Math.Min(.7f,model.Radii[p]));
                if(step%200==0||step+1==settings.FitSteps)progress?.Invoke($"Fit {step+1}/{settings.FitSteps}: total={gradient.TotalLoss:F6}, body/fill={gradient.PositiveLoss:F6}, empty={gradient.NegativeLoss:F6}, rays={gradient.RayLoss:F6}");
            }
            return model;
        }
    }
}
