using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace July.Physics.Authoring
{
    // Pure geometry: no Mesh creation, AssetDatabase writes, or Unity objects on the worker thread.
    internal static class PrimitiveColliderFitter
    {
        internal enum Kind { Box, Sphere, Capsule }

        internal sealed class Shape
        {
            public Kind Type;
            public Vector3 Center;
            public Quaternion Rotation = Quaternion.identity;
            public Vector3 Size;
            public float Radius;
            public float Height;
            public float Volume;
        }

        internal sealed class Result
        {
            public Shape[] Shapes;
            public float ExcessVolumeRatio;
        }

        private sealed class Part
        {
            public Vector3[] Points;
            public Quaternion Frame;
            public Shape Shape;
            public Part[][] Splits;
        }

        internal static Result Fit(ColliderGeometry.Geometry geometry, ColliderGenerationSettings settings)
        {
            var points = Voxelize(geometry, out var cell);
            var cellVolume = cell * cell * cell;
            var occupiedVolume = points.Length * cellVolume;
            var best = new List<Part> { FitPart(points, cell) };
            var bestVolume = best[0].Shape.Volume;
            var plans = new List<List<Part>> { best };
            float Excess(float volume) => Mathf.Max(0, volume / occupiedVolume - 1);
            // Retain four alternatives. A ring needs several cuts before any volume saving appears;
            // refusing each neutral first cut would leave its hole covered by one large box.
            for (var count = 1; count < settings.MaxShapes && Excess(bestVolume) > settings.Tolerance; count++)
            {
                var next = new List<List<Part>>();
                foreach (var plan in plans)
                foreach (var part in plan)
                {
                    if (part.Shape.Volume <= part.Points.Length * cellVolume * (1 + settings.Tolerance)) continue;
                    if (part.Splits == null) part.Splits = FindSplits(part, cell);
                    foreach (var split in part.Splits)
                    {
                        var candidate = new List<Part>(plan);
                        var index = candidate.IndexOf(part);
                        candidate.RemoveAt(index);
                        candidate.InsertRange(index, split);
                        next.Add(candidate);
                    }
                }
                if (next.Count == 0) break;
                plans = next.OrderBy(plan => plan.Sum(part => part.Shape.Volume)).Take(4).ToList();
                var volume = plans[0].Sum(part => part.Shape.Volume);
                // Spend extra shapes only if the complete plan saves at least one percent.
                if (volume < bestVolume - occupiedVolume * 0.01f) { best = plans[0]; bestVolume = volume; }
            }
            return new Result { Shapes = best.Select(p => p.Shape).ToArray(), ExcessVolumeRatio = Excess(bestVolume) };
        }

        private static Part FitPart(Vector3[] points, float cell)
        {
            var frame = PrincipalFrame(points, out var mean);
            var best = FitBox(points, cell, Quaternion.identity);
            Choose(ref best, FitBox(points, cell, frame));
            var bounds = BoundsOf(points);
            foreach (var center in new[] { mean, bounds.center })
            {
                var radius = Mathf.Sqrt(points.Max(p => (p - center).sqrMagnitude)) + cell * Mathf.Sqrt(3) * 0.5f;
                Choose(ref best, new Shape { Type = Kind.Sphere, Center = center, Radius = radius,
                    Volume = 4f / 3f * Mathf.PI * radius * radius * radius });
                foreach (var axis in new[] { frame * Vector3.right, Vector3.right, Vector3.up, Vector3.forward })
                    Choose(ref best, FitCapsule(points, center, axis, cell));
            }
            return new Part { Points = points, Frame = frame, Shape = best };
        }

        private static void Choose(ref Shape best, Shape candidate)
        {
            if (candidate.Volume < best.Volume * 0.9999f) best = candidate;
        }

        private static Shape FitBox(Vector3[] points, float cell, Quaternion rotation)
        {
            var inverse = Quaternion.Inverse(rotation);
            var bounds = new Bounds(inverse * points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(inverse * point);
            float Padding(Vector3 axis) { axis = rotation * axis; return cell * (Mathf.Abs(axis.x) + Mathf.Abs(axis.y) + Mathf.Abs(axis.z)); }
            var size = bounds.size + new Vector3(Padding(Vector3.right), Padding(Vector3.up), Padding(Vector3.forward));
            return new Shape { Type = Kind.Box, Center = rotation * bounds.center, Rotation = rotation,
                Size = size, Volume = size.x * size.y * size.z };
        }

        private static Shape FitCapsule(Vector3[] points, Vector3 center, Vector3 axis, float cell)
        {
            var radial = 0f;
            foreach (var point in points)
            {
                var offset = point - center;
                radial = Mathf.Max(radial, (offset - axis * Vector3.Dot(offset, axis)).sqrMagnitude);
            }
            var radius = Mathf.Sqrt(radial);
            var segment = 0f;
            foreach (var point in points)
            {
                var offset = point - center;
                var along = Vector3.Dot(offset, axis);
                var distance = (offset - axis * along).sqrMagnitude;
                segment = Mathf.Max(segment, Mathf.Abs(along) - Mathf.Sqrt(Mathf.Max(0, radius * radius - distance)));
            }
            radius += cell * Mathf.Sqrt(3) * 0.5f;
            return new Shape { Type = Kind.Capsule, Center = center, Rotation = Quaternion.FromToRotation(Vector3.up, axis),
                Radius = radius, Height = 2 * (segment + radius),
                Volume = Mathf.PI * radius * radius * (2 * segment + 4f / 3f * radius) };
        }

        private static Part[][] FindSplits(Part part, float cell)
        {
            var axes = new List<Vector3>();
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward,
                part.Frame * Vector3.right, part.Frame * Vector3.up, part.Frame * Vector3.forward })
                if (!axes.Any(previous => Mathf.Abs(Vector3.Dot(previous, axis)) > 0.999f)) axes.Add(axis);
            var candidates = new List<Part[]>();
            foreach (var axis in axes)
            {
                var sorted = part.Points.OrderBy(p => Vector3.Dot(p, axis)).ToArray();
                var bins = new List<int> { 0 };
                for (var i = 1; i < sorted.Length; i++)
                    if (Mathf.Abs(Vector3.Dot(sorted[i] - sorted[i - 1], axis)) >= cell * 0.001f) bins.Add(i);
                bins.Add(sorted.Length);
                // Cross-section density changes locate narrow branches and ring sides.
                var cuts = Enumerable.Range(1, bins.Count - 2).OrderByDescending(i =>
                    Math.Abs((bins[i] - bins[i - 1]) - (bins[i + 1] - bins[i]))).Take(2).Select(i => bins[i]).ToList();
                foreach (var fraction in new[] { 0.25f, 0.5f, 0.75f })
                {
                    var index = (int)(sorted.Length * fraction);
                    if (index == 0 || index == sorted.Length) continue;
                    var value = Vector3.Dot(sorted[index], axis);
                    while (index > 0 && Mathf.Abs(Vector3.Dot(sorted[index - 1], axis) - value) < cell * 0.001f) index--;
                    if (index == 0) continue;
                    cuts.Add(index);
                }
                foreach (var index in cuts.Distinct())
                    candidates.Add(new[] { FitPart(sorted.Take(index).ToArray(), cell), FitPart(sorted.Skip(index).ToArray(), cell) });
            }
            return candidates.OrderBy(split => split[0].Shape.Volume + split[1].Shape.Volume).Take(4).ToArray();
        }

        private static Bounds BoundsOf(Vector3[] points)
        {
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            return bounds;
        }

        private static Quaternion PrincipalFrame(Vector3[] points, out Vector3 mean)
        {
            mean = Vector3.zero;
            foreach (var point in points) mean += point;
            mean /= points.Length;
            var matrix = new double[3, 3];
            foreach (var point in points)
            {
                var delta = point - mean;
                for (var row = 0; row < 3; row++)
                    for (var col = 0; col < 3; col++) matrix[row, col] += (double)delta[row] * delta[col];
            }
            var vectors = new double[3, 3];
            for (var i = 0; i < 3; i++) vectors[i, i] = 1;
            for (var iteration = 0; iteration < 24; iteration++)
            {
                var p = 0; var q = 1;
                for (var row = 0; row < 3; row++)
                    for (var col = row + 1; col < 3; col++)
                        if (Math.Abs(matrix[row, col]) > Math.Abs(matrix[p, q])) { p = row; q = col; }
                if (Math.Abs(matrix[p, q]) < 1e-12) break;
                var angle = 0.5 * Math.Atan2(2 * matrix[p, q], matrix[q, q] - matrix[p, p]);
                var c = Math.Cos(angle); var s = Math.Sin(angle);
                for (var row = 0; row < 3; row++)
                {
                    var a = matrix[row, p]; var b = matrix[row, q];
                    matrix[row, p] = c * a - s * b; matrix[row, q] = s * a + c * b;
                    a = vectors[row, p]; b = vectors[row, q];
                    vectors[row, p] = c * a - s * b; vectors[row, q] = s * a + c * b;
                }
                for (var col = 0; col < 3; col++)
                {
                    var a = matrix[p, col]; var b = matrix[q, col];
                    matrix[p, col] = c * a - s * b; matrix[q, col] = s * a + c * b;
                }
            }
            var order = Enumerable.Range(0, 3).OrderByDescending(i => matrix[i, i]).ToArray();
            Vector3 Axis(int i) => new Vector3((float)vectors[0, i], (float)vectors[1, i], (float)vectors[2, i]).normalized;
            var x = Axis(order[0]); var y = Axis(order[1]);
            return Quaternion.LookRotation(Vector3.Cross(x, y).normalized, y);
        }

        // Conservative triangle/voxel intersection plus exterior flood fill. Union across all sources
        // preserves enclosed volumes and holes without assuming consistent winding or one connected mesh.
        private static Vector3[] Voxelize(ColliderGeometry.Geometry geometry, out float cell)
        {
            var bounds = BoundsOf(geometry.Vertices);
            cell = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) / 32;
            if (cell <= 0) throw new ArgumentException("来源几何没有可拟合的尺寸。");
            var step = cell;
            var origin = bounds.min - Vector3.one * (2 * step);
            var nx = Mathf.CeilToInt(bounds.size.x / step) + 4;
            var ny = Mathf.CeilToInt(bounds.size.y / step) + 4;
            var nz = Mathf.CeilToInt(bounds.size.z / step) + 4;
            var occupied = new bool[nx * ny * nz];
            int Index(int x, int y, int z) => (z * ny + y) * nx + x;
            Vector3 Center(int x, int y, int z) => origin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * step;
            for (var i = 0; i < geometry.Indices.Length; i += 3)
            {
                var a = geometry.Vertices[geometry.Indices[i]];
                var b = geometry.Vertices[geometry.Indices[i + 1]];
                var c = geometry.Vertices[geometry.Indices[i + 2]];
                var min = (Vector3.Min(a, Vector3.Min(b, c)) - origin) / step;
                var max = (Vector3.Max(a, Vector3.Max(b, c)) - origin) / step;
                for (var z = Mathf.FloorToInt(min.z); z <= Mathf.FloorToInt(max.z); z++)
                    for (var y = Mathf.FloorToInt(min.y); y <= Mathf.FloorToInt(max.y); y++)
                        for (var x = Mathf.FloorToInt(min.x); x <= Mathf.FloorToInt(max.x); x++)
                        {
                            var index = Index(x, y, z);
                            if (!occupied[index] && Intersects(a, b, c, Center(x, y, z), step * 0.5f)) occupied[index] = true;
                        }
            }
            var exterior = new bool[occupied.Length];
            var queue = new Queue<int>();
            queue.Enqueue(0); exterior[0] = true;
            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % nx; var y = index / nx % ny; var z = index / (nx * ny);
                void Visit(int next)
                {
                    if (occupied[next] || exterior[next]) return;
                    exterior[next] = true; queue.Enqueue(next);
                }
                if (x > 0) Visit(index - 1);
                if (x + 1 < nx) Visit(index + 1);
                if (y > 0) Visit(index - nx);
                if (y + 1 < ny) Visit(index + nx);
                if (z > 0) Visit(index - nx * ny);
                if (z + 1 < nz) Visit(index + nx * ny);
            }
            var result = new List<Vector3>();
            for (var z = 0; z < nz; z++)
                for (var y = 0; y < ny; y++)
                    for (var x = 0; x < nx; x++)
                        if (!exterior[Index(x, y, z)]) result.Add(Center(x, y, z));
            if (result.Count == 0) throw new ArgumentException("来源三角形没有可拟合的表面。");
            return result.ToArray();
        }

        private static bool Intersects(Vector3 a, Vector3 b, Vector3 c, Vector3 center, float half)
        {
            a -= center; b -= center; c -= center;
            bool Separates(Vector3 axis)
            {
                var radius = half * (Mathf.Abs(axis.x) + Mathf.Abs(axis.y) + Mathf.Abs(axis.z));
                var pa = Vector3.Dot(axis, a); var pb = Vector3.Dot(axis, b); var pc = Vector3.Dot(axis, c);
                return Mathf.Min(pa, Mathf.Min(pb, pc)) > radius || Mathf.Max(pa, Mathf.Max(pb, pc)) < -radius;
            }
            if (Separates(Vector3.right) || Separates(Vector3.up) || Separates(Vector3.forward) || Separates(Vector3.Cross(b - a, c - a))) return false;
            bool EdgeSeparates(Vector3 edge) => Separates(Vector3.Cross(edge, Vector3.right)) ||
                Separates(Vector3.Cross(edge, Vector3.up)) || Separates(Vector3.Cross(edge, Vector3.forward));
            return !EdgeSeparates(b - a) && !EdgeSeparates(c - b) && !EdgeSeparates(a - c);
        }
    }
}
