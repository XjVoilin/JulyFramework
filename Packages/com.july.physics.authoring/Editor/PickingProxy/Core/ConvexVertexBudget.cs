#nullable enable
using System;
using System.Linq;
using System.Threading;

namespace July.Physics.Authoring.PickingProxy
{
    internal static class ConvexVertexBudget
    {
        // Add the worst-covered original corner, keeping error distributed.
        // All output vertices belong to the normalized original convex hull.
        internal static TriangleMesh Reduce(TriangleMesh input, int maximum, double inwardLimit,
            CancellationToken token = default)
        {
            if (maximum < 4) throw new ArgumentOutOfRangeException(nameof(maximum));
            token.ThrowIfCancellationRequested();
            var original = ConvexHull.Build(input.Vertices);
            if (original.Mesh.Vertices.Length <= maximum)
            {
                ConvexMeshExport.Validate(original.Mesh);
                return original.Mesh;
            }
            var points = original.Mesh.Vertices;
            int a = 0;
            for (int i = 1; i < points.Length; i++) if (points[i].X < points[a].X) a = i;
            int b = Enumerable.Range(0, points.Length).OrderByDescending(i => (points[i]-points[a]).LengthSquared).First();
            int c = Enumerable.Range(0, points.Length).OrderByDescending(i => Vec3.Cross(points[b]-points[a], points[i]-points[a]).LengthSquared).First();
            var plane = Plane3.Triangle(points[a], points[b], points[c]);
            int d = Enumerable.Range(0, points.Length).OrderByDescending(i => Math.Abs(plane.At(points[i]))).First();
            var current = ConvexHull.Build(new[] { points[a], points[b], points[c], points[d] });
            while (current.Mesh.Vertices.Length < maximum)
            {
                token.ThrowIfCancellationRequested();
                var query = new MeshQueries(current.Mesh);
                int worst = -1; double error = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    if (current.Planes.All(p => p.At(points[i]) <= 1e-10)) continue;
                    double distance = query.Distance(points[i]);
                    if (distance > error) { error = distance; worst = i; }
                }
                if (worst < 0 || error < 1e-9) break;
                current = ConvexHull.Build(current.Mesh.Vertices.Concat(new[] { points[worst] }).ToArray());
            }
            double deviation = ConvexMeshExport.InwardDeviation(points, current.Mesh, current.Planes);
            if (deviation > inwardLimit)
                throw new InvalidOperationException($"顶点预算{maximum}的内缩误差{deviation * 100:0.###}%超过允许值{inwardLimit * 100:0.###}%（模型最长边）；请提高每包顶点上限或调整允许内缩。已有候选保留。");
            ConvexMeshExport.Validate(current.Mesh);
            return current.Mesh;
        }
    }
}
