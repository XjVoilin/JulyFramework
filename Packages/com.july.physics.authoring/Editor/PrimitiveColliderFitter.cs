using System;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace July.Physics.Authoring
{
    internal static class PrimitiveColliderFitter
    {
        static readonly Kind[] Kinds = { Kind.Box, Kind.Sphere, Kind.Capsule, Kind.Cylinder, Kind.Frustum, Kind.Prism };
        static readonly string[] KindNames = { "盒", "球", "胶囊", "圆柱", "圆台", "梯形棱柱" };

        internal enum Kind
        {
            Box = 0,
            Sphere = 1,
            Capsule = 2,
            // Historical irregular-convex value 3 is intentionally not reused.
            Cylinder = 4,
            Frustum = 5,
            Prism = 6,
            Convex = 7
        }

        [Serializable]
        internal sealed class Shape
        {
            public Kind Type;
            public Vector3 Center, Size;
            public Quaternion Rotation = Quaternion.identity;
            public float Radius, Height, Bottom, Top, Volume;
            internal Vector4[] Planes;
            public Vector3[] ConvexVertices;
            public int[] ConvexTriangles;
        }

        internal sealed class Result
        {
            internal Shape[] Shapes;
            internal double GenerationSeconds;
            internal string Note;
        }

        internal static Result Fit(ColliderGeometry.Geometry mesh, ColliderGenerationSettings settings, ColliderFitProgress progress, CancellationToken token, ColliderGpuScoring scoring = null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var seed = ColliderDecomposition.Fit(mesh, settings, progress, token, scoring);
            var result = new Result { Shapes = seed.Shapes, GenerationSeconds = timer.Elapsed.TotalSeconds };
            string counts = DescribeComposition(result.Shapes);
            string reference = settings.Goal == ColliderGeometryGoal.Structure ? "六向原始剪影" : "六向外包轮廓（不保留空隙）";
            string backend = scoring != null && scoring.DispatchBatches > 0 ? "GPU轮廓评分" : "CPU";
            result.Note = $"原生三类 · {backend} · {settings.Goal}：{result.Shapes.Length}个，{counts}，{result.GenerationSeconds:F2}秒。" +
                $"\n参考{reference}：漏包{seed.MissingPercent:F1}%／外扩{seed.ExtraPercent:F1}%，预算{settings.SilhouetteErrorPercent:F1}%。";
            return result;
        }

        internal static string DescribeComposition(Shape[] shapes) => string.Join(" · ",
            Kinds.Select((kind, i) => (name: KindNames[i], count: shapes.Count(s => s.Type == kind)))
                .Concat(new[] { (name: "凸包", count: shapes.Count(s => s.Type == Kind.Convex)) })
                .Where(value => value.count > 0).Select(value => $"{value.name}×{value.count}"));

        internal static bool Contains(Shape s, Vector3 p)
        {
            var rotation = s.Rotation;
            var q = new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w) * (p - s.Center);
            if (s.Type == Kind.Convex)
            {
                for (int i = 0; i < s.ConvexTriangles.Length; i += 3)
                {
                    var a = s.ConvexVertices[s.ConvexTriangles[i]];
                    var normal = Vector3.Cross(s.ConvexVertices[s.ConvexTriangles[i + 1]] - a, s.ConvexVertices[s.ConvexTriangles[i + 2]] - a);
                    if (Vector3.Dot(normal, q - a) > 1e-7f * normal.magnitude) return false;
                }
                return true;
            }
            if (s.Type == Kind.Box)
                return Mathf.Abs(q.x) <= s.Size.x * .5f + 1e-7f && Mathf.Abs(q.y) <= s.Size.y * .5f + 1e-7f && Mathf.Abs(q.z) <= s.Size.z * .5f + 1e-7f;
            if (SixPrimitiveMeshes.Regular(s))
                return SixPrimitiveMeshes.ContainsBall(s, q, 0);
            if (s.Type == Kind.Capsule)
                q.y -= Mathf.Clamp(q.y, -Mathf.Max(0, s.Height * .5f - s.Radius), Mathf.Max(0, s.Height * .5f - s.Radius));
            return q.sqrMagnitude <= s.Radius * s.Radius + 1e-9f;
        }
    }
}
