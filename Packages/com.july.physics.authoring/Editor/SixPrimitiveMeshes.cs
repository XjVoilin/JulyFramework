using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Shape = July.Physics.Authoring.PrimitiveColliderFitter.Shape;
using Kind = July.Physics.Authoring.PrimitiveColliderFitter.Kind;
using Primitive = July.Physics.Authoring.SixPrimitiveMath.Primitive;
using PrimitiveKind = July.Physics.Authoring.SixPrimitiveMath.PrimitiveKind;

namespace July.Physics.Authoring
{
    // Mesh adapter for existing regular-shape resources and typed previews.
    // Automatic envelope fitting only emits native boxes, spheres and capsules.
    internal static class SixPrimitiveMeshes
    {
        internal const int RadialSegments = 32;

        internal static Shape Standardize(Shape shape, out Vector3 scale)
        {
            var unit = new Primitive { Rotation = Quaternion.identity };
            switch (shape.Type)
            {
                case Kind.Cylinder:
                    unit.Kind = PrimitiveKind.Cylinder; unit.Radius = 1; unit.Height = 1;
                    scale = new Vector3(shape.Radius, shape.Height, shape.Radius);
                    break;
                case Kind.Frustum:
                    float radius = Mathf.Max(shape.Bottom, shape.Top);
                    unit.Kind = PrimitiveKind.Frustum; unit.Height = 1;
                    unit.Bottom = shape.Bottom / radius; unit.Top = shape.Top / radius;
                    scale = new Vector3(radius, shape.Height, radius);
                    break;
                case Kind.Prism:
                    float depth = Mathf.Max(shape.Bottom, shape.Top);
                    unit.Kind = PrimitiveKind.Prism; unit.HalfSize = Vector3.one;
                    unit.Bottom = shape.Bottom / depth; unit.Top = shape.Top / depth;
                    scale = new Vector3(shape.Size.x * .5f, shape.Size.y * .5f, depth);
                    break;
                default:
                    throw new ArgumentException("只有圆柱、圆台和等腰梯形棱柱使用标准Mesh。", nameof(shape));
            }
            return Convert(unit);
        }

        internal static Shape Convert(Primitive p)
        {
            var result = new Shape { Center = p.Center, Rotation = p.Rotation, Volume = (float)p.Volume };
            switch (p.Kind)
            {
                case PrimitiveKind.Box: result.Type = Kind.Box; result.Size = p.HalfSize * 2; return result;
                case PrimitiveKind.Sphere: result.Type = Kind.Sphere; result.Radius = p.Radius; return result;
                case PrimitiveKind.Capsule:
                    result.Type = Kind.Capsule; result.Radius = p.Radius; result.Height = p.Height + 2 * p.Radius; return result;
                case PrimitiveKind.Cylinder:
                case PrimitiveKind.Frustum:
                    RoundMesh(p, out result.ConvexVertices, out result.ConvexTriangles); break;
                case PrimitiveKind.Prism:
                    PrismMesh(p, out result.ConvexVertices, out result.ConvexTriangles); break;
                default: throw new ArgumentOutOfRangeException();
            }
            result.Type = ExtraKind(p.Kind);
            result.Bottom=p.Bottom; result.Top=p.Top; result.Height=p.Height; result.Radius=p.Radius;
            var low = result.ConvexVertices[0]; var high = low;
            foreach (var v in result.ConvexVertices) { low = Vector3.Min(low, v); high = Vector3.Max(high, v); }
            result.Size = high - low;
            return result;
        }

        private static void RoundMesh(Primitive p, out Vector3[] vertices, out int[] triangles)
        {
            vertices = new Vector3[RadialSegments * 2];
            // A tangent polygon conservatively quantizes a circle for Unity's convex mesh collider.
            float correction = 1 / Mathf.Cos(Mathf.PI / RadialSegments);
            for (int ring = 0; ring < 2; ring++)
            {
                float radius = (p.Kind == PrimitiveKind.Cylinder ? p.Radius : ring == 0 ? p.Bottom : p.Top) * correction;
                for (int i = 0; i < RadialSegments; i++)
                {
                    float angle = (i + .5f) * 2 * Mathf.PI / RadialSegments;
                    vertices[ring * RadialSegments + i] = new Vector3(radius * Mathf.Cos(angle),
                        (ring - .5f) * p.Height, radius * Mathf.Sin(angle));
                }
            }
            var faces = new List<int>();
            for (int i = 0; i < RadialSegments; i++)
            {
                int next = (i + 1) % RadialSegments;
                faces.AddRange(new[] { i, i + RadialSegments, next, next, i + RadialSegments, next + RadialSegments });
            }
            for (int i = 1; i < RadialSegments - 1; i++)
            {
                faces.AddRange(new[] { 0, i, i + 1, RadialSegments, RadialSegments + i + 1, RadialSegments + i });
            }
            triangles = faces.ToArray();
        }

        private static void PrismMesh(Primitive p, out Vector3[] vertices, out int[] triangles)
        {
            vertices = new Vector3[8];
            for (int z = 0; z < 2; z++) for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
                vertices[z * 4 + y * 2 + x] = new Vector3((2 * x - 1) * p.HalfSize.x,
                    (2 * y - 1) * p.HalfSize.y, (2 * z - 1) * (y == 0 ? p.Bottom : p.Top));
            triangles = new[] { 0,2,1, 1,2,3, 4,5,6, 5,7,6, 0,1,4, 1,5,4,
                2,6,3, 3,6,7, 0,4,2, 2,4,6, 1,3,5, 3,7,5 };
        }
        internal static bool Regular(Shape s)=>s.Type==Kind.Cylinder||s.Type==Kind.Frustum||s.Type==Kind.Prism;
        static Kind ExtraKind(PrimitiveKind kind)=>kind==PrimitiveKind.Cylinder?Kind.Cylinder:kind==PrimitiveKind.Frustum?Kind.Frustum:Kind.Prism;
        internal static bool ContainsBall(Shape s,Vector3 local,float radius)
        {
            if(s.Planes==null)
            {
                var center=s.ConvexVertices.Aggregate(Vector3.zero,(a,b)=>a+b)/s.ConvexVertices.Length;
                var planes=new List<Vector4>();
                for(int i=0;i<s.ConvexTriangles.Length;i+=3)
                {
                    var a=s.ConvexVertices[s.ConvexTriangles[i]];
                    var n=Vector3.Cross(s.ConvexVertices[s.ConvexTriangles[i+1]]-a,s.ConvexVertices[s.ConvexTriangles[i+2]]-a).normalized;
                    if(Vector3.Dot(n,center-a)>0)n=-n;
                    var plane=new Vector4(n.x,n.y,n.z,Vector3.Dot(n,a));
                    // Planar adjacent triangles share a plane. Remove only numerically identical copies.
                    if(!planes.Any(p=>Mathf.Abs(p.x-plane.x)<1e-6f&&Mathf.Abs(p.y-plane.y)<1e-6f&&Mathf.Abs(p.z-plane.z)<1e-6f&&Mathf.Abs(p.w-plane.w)<1e-6f))planes.Add(plane);
                }
                s.Planes=planes.ToArray();
            }
            foreach(var plane in s.Planes)
                if(plane.x*local.x+plane.y*local.y+plane.z*local.z+radius>plane.w+1e-7f)return false;
            return true;
        }
    }
}
