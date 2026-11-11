using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Shape = July.Physics.Authoring.PrimitiveColliderFitter.Shape;
using Kind = July.Physics.Authoring.PrimitiveColliderFitter.Kind;

namespace July.Physics.Authoring
{
    /// <summary>
    /// CPD 区域初始化，依据 Knodt/Gao 2026 §3 独立实现：逐面区域、面积加权法线矩阵、
    /// 特征轴拟合、最小体积增量优先合并作为起点；容差模式另按组合轮廓拟合和简化。
    /// 只拟合原生盒、球和胶囊，无体素补形、不规则凸包或资源分类模板。
    /// https://arxiv.org/abs/2602.07369
    /// </summary>
    internal static partial class ColliderDecomposition
    {
        internal sealed class Result
        {
            internal Shape[] Shapes;
            internal double Seconds;
            internal float MissingPercent, ExtraPercent;
        }

        // Six independent coefficients of the symmetric, additive face quadric.
        private struct Quadric
        {
            internal double XX, XY, XZ, YY, YZ, ZZ;
            internal static Quadric Outer(Vector3 v, double weight) => new Quadric{XX = weight * v.x * v.x, XY = weight * v.x * v.y, XZ = weight * v.x * v.z, YY = weight * v.y * v.y, YZ = weight * v.y * v.z, ZZ = weight * v.z * v.z};
            public static Quadric operator +(Quadric a, Quadric b) => new Quadric{XX = a.XX + b.XX, XY = a.XY + b.XY, XZ = a.XZ + b.XZ, YY = a.YY + b.YY, YZ = a.YZ + b.YZ, ZZ = a.ZZ + b.ZZ};
        }

        private sealed class Region
        {
            internal int[] Vertices;
            internal Quadric Q;
            internal Shape Shape;
            // Input vertices and Q are fixed once contour fitting begins.
            internal Shape[] Candidates;
            internal readonly HashSet<int> Neighbours = new HashSet<int>();
            internal int Version;
            internal bool Active = true;
        }

        private sealed class Merge
        {
            internal int A, B, VersionA, VersionB;
            internal double Cost;
            internal Shape Shape;
        }

        // Unity 2022's API profile does not provide System.Collections.Generic.PriorityQueue.
        private sealed class MergeQueue
        {
            private readonly List<Merge> _heap = new List<Merge>();
            internal int Count => _heap.Count;
            private static bool Before(Merge a, Merge b) => a.Cost < b.Cost || (a.Cost == b.Cost && (a.A < b.A || (a.A == b.A && a.B < b.B)));
            internal void Push(Merge value)
            {
                int i = _heap.Count;
                _heap.Add(value);
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Before(value, _heap[parent]))
                        break;
                    _heap[i] = _heap[parent];
                    i = parent;
                }

                _heap[i] = value;
            }

            internal Merge Pop()
            {
                var first = _heap[0];
                var last = _heap[_heap.Count - 1];
                _heap.RemoveAt(_heap.Count - 1);
                if (_heap.Count == 0)
                    return first;
                int i = 0;
                while (i * 2 + 1 < _heap.Count)
                {
                    int child = i * 2 + 1;
                    if (child + 1 < _heap.Count && Before(_heap[child + 1], _heap[child]))
                        child++;
                    if (!Before(_heap[child], last))
                        break;
                    _heap[i] = _heap[child];
                    i = child;
                }

                _heap[i] = last;
                return first;
            }
        }

        internal static Result Fit(ColliderGeometry.Geometry geometry, ColliderGenerationSettings settings, ColliderFitProgress progress = null, CancellationToken token = default, ColliderGpuScoring scoring = null)
        {
            settings.Validate();
            token.ThrowIfCancellationRequested();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var bounds = new Bounds(geometry.Vertices[0], Vector3.zero);
            foreach (var p in geometry.Vertices)
                bounds.Encapsulate(p);
            float scale = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (scale < 1e-8f)
                throw new ArgumentException("模型尺寸过小或没有有效表面。");
            Vector3 origin = bounds.center;
            float minimumSize = settings.MinimumThicknessPercent * .01f;
            // Exact position welding joins UV/normal seams without collapsing small gaps.
            progress?.Report(0, "读取三角面与位置接缝");
            var positions = new List<Vector3>();
            var unique = new Dictionary<Vector3, int>();
            var remap = new int[geometry.Vertices.Length];
            for (int i = 0; i < remap.Length; i++)
            {
                Vector3 p = (geometry.Vertices[i] - origin) / scale;
                if (!unique.TryGetValue(p, out int id))
                {
                    id = positions.Count;
                    unique.Add(p, id);
                    positions.Add(p);
                }

                remap[i] = id;
                if ((i & 255) == 0) progress?.Report(.04f * i / remap.Length, $"读取顶点：{i}/{remap.Length}");
            }

            var points = positions.ToArray();
            var weights = new float[points.Length];
            var regions = new List<Region>();
            var edges = new Dictionary<(int, int), List<int>>();
            void Edge(int a, int b, int face)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var owners))
                    edges.Add(key, owners = new List<int>());
                foreach (int other in owners)
                {
                    regions[face].Neighbours.Add(other);
                    regions[other].Neighbours.Add(face);
                }

                owners.Add(face);
            }

            for (int i = 0; i < geometry.Indices.Length; i += 3)
            {
                if ((i % 768) == 0)
                    progress?.Report(.04f + .06f * i / geometry.Indices.Length, $"建立面片区域：{i / 3}/{geometry.Indices.Length / 3}");
                token.ThrowIfCancellationRequested();
                int a = remap[geometry.Indices[i]], b = remap[geometry.Indices[i + 1]], c = remap[geometry.Indices[i + 2]];
                Vector3 cross = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                float twiceArea = cross.magnitude;
                if (twiceArea <= 1e-12f)
                    continue;
                weights[a] += twiceArea / 6;
                weights[b] += twiceArea / 6;
                weights[c] += twiceArea / 6;
                var q = Quadric.Outer(cross / twiceArea, twiceArea * .5);
                var ids = new[]{a, b, c};
                Array.Sort(ids);
                var region = new Region{Vertices = ids, Q = q};
                region.Shape = FitRegion(points, ids, q, minimumSize);
                int face = regions.Count;
                regions.Add(region);
                Edge(a, b, face);
                Edge(b, c, face);
                Edge(c, a, face);
            }

            if (regions.Count == 0)
                throw new ArgumentException("来源模型没有非退化三角面。");
            var queue = new MergeQueue();
            void Enqueue(int a, int b)
            {
                token.ThrowIfCancellationRequested();
                if (a > b)
                {
                    int swap = a;
                    a = b;
                    b = swap;
                }

                var left = regions[a];
                var right = regions[b];
                var shape = FitRegion(points, Union(left.Vertices, right.Vertices), left.Q + right.Q, minimumSize);
                double cost = (double)shape.Volume - left.Shape.Volume - right.Shape.Volume;
                queue.Push(new Merge{A = a, B = b, VersionA = left.Version, VersionB = right.Version, Cost = cost, Shape = shape});
            }

            int pairCount = regions.Sum(r => r.Neighbours.Count) / 2, pairsDone = 0;
            progress?.Report(.1f, $"建立合并候选：0/{pairCount}");
            for (int i = 0; i < regions.Count; i++)
                foreach (int next in regions[i].Neighbours.OrderBy(n => n))
                    if (next > i)
                    {
                        Enqueue(i, next);
                        if ((++pairsDone & 63) == 0)
                            progress?.Report(.1f + .12f * pairsDone / pairCount, $"建立合并候选：{pairsDone}/{pairCount}");
                    }
            int count = regions.Count, merges = 0;
            int mergeWork = Math.Max(1, regions.Count - settings.TargetCount);
            progress?.Report(.22f, $"合并区域：{count} → {settings.TargetCount}");
            bool allPairs = false;
            while (count > settings.TargetCount)
            {
                token.ThrowIfCancellationRequested();
                if (queue.Count == 0)
                {
                    if (allPairs)
                        break;
                    // Paper §3.4: after topological merges, allow disconnected components to merge.
                    allPairs = true;
                    progress?.Report(.22f + .38f * merges / mergeWork, $"评估跨区域合并：剩余 {count} 个区域");
                    var active = Enumerable.Range(0, regions.Count).Where(i => regions[i].Active).ToArray();
                    long crossWork = (long)active.Length * (active.Length - 1) / 2, crossDone = 0;
                    for (int i = 0; i < active.Length; i++)
                        for (int j = i + 1; j < active.Length; j++)
                        {
                            regions[active[i]].Neighbours.Add(active[j]);
                            regions[active[j]].Neighbours.Add(active[i]);
                            Enqueue(active[i], active[j]);
                            if ((++crossDone & 63) == 0)
                                progress?.Report(.22f + .38f * (merges + .5f * crossDone / crossWork) / mergeWork,
                                    $"评估跨区域候选：{crossDone}/{crossWork}");
                        }

                    if (queue.Count == 0)
                        break;
                }

                var merge = queue.Pop();
                var left = regions[merge.A];
                var right = regions[merge.B];
                if (!left.Active || !right.Active || left.Version != merge.VersionA || right.Version != merge.VersionB)
                    continue;
                left.Vertices = Union(left.Vertices, right.Vertices);
                left.Q += right.Q;
                left.Shape = merge.Shape;
                left.Version++;
                right.Active = false;
                left.Neighbours.UnionWith(right.Neighbours);
                left.Neighbours.Remove(merge.A);
                left.Neighbours.Remove(merge.B);
                foreach (int next in left.Neighbours.OrderBy(n => n))
                {
                    var neighbour = regions[next];
                    if (!neighbour.Active)
                        continue;
                    neighbour.Neighbours.Remove(merge.B);
                    neighbour.Neighbours.Add(merge.A);
                    Enqueue(merge.A, next);
                }

                count--;
                merges++;
                if (merges % 128 == 0)
                    progress?.Report(.22f + .38f * merges / mergeWork, $"合并中：{count} 个区域，目标 {settings.TargetCount}");
            }

            var result = new Result();
            var activeRegions = regions.Where(r => r.Active).ToArray();
            var shapes = activeRegions.Select(r => r.Shape).ToArray();
            if (settings.SilhouetteErrorPercent != 0)
                shapes = Simplify(points, geometry.Indices.Select(i => remap[i]).ToArray(), weights, activeRegions, settings, result, progress?.Slice(.6f, .98f), token, scoring);
            shapes = RemoveContained(shapes, token);

            foreach (var shape in shapes)
            {
                shape.Center = shape.Center * scale + origin;
                shape.Size *= scale;
                shape.Radius *= scale;
                shape.Height *= scale;
                shape.Volume *= scale * scale * scale;
            }

            result.Shapes = shapes;
            result.Seconds = timer.Elapsed.TotalSeconds;
            progress?.Report(1, $"主体拟合完成：{shapes.Length} 个形状");
            return result;
        }

        private static int[] Union(int[] a, int[] b)
        {
            var result = new int[a.Length + b.Length];
            int i = 0, j = 0, length = 0;
            while (i < a.Length || j < b.Length)
            {
                int next;
                if (j == b.Length || (i < a.Length && a[i] < b[j]))
                    next = a[i++];
                else if (i == a.Length || b[j] < a[i])
                    next = b[j++];
                else
                {
                    next = a[i++];
                    j++;
                }

                result[length++] = next;
            }

            Array.Resize(ref result, length);
            return result;
        }

        private static Shape FitRegion(Vector3[] vertices, int[] ids, Quadric q, float minimumSize)
        {
            Quaternion rotation = Frame(q);
            var inverse = new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w);
            Vector3 low = inverse * vertices[ids[0]], high = low;
            foreach (int id in ids)
            {
                Vector3 p = inverse * vertices[id];
                low = Vector3.Min(low, p);
                high = Vector3.Max(high, p);
            }

            Vector3 center = rotation * ((low + high) * .5f);
            // Paper §3.2 also clamps zero-volume dimensions. Normalize by item size for consistent author controls.
            Vector3 size = Vector3.Max(high - low + Vector3.one * 2e-6f, Vector3.one * minimumSize);
            var best = new Shape{Type = Kind.Box, Center = center, Rotation = rotation, Size = size, Volume = size.x * size.y * size.z};
            float radiusSquared = 0;
            foreach (int id in ids)
                radiusSquared = Mathf.Max(radiusSquared, (vertices[id] - center).sqrMagnitude);
            float radius = Mathf.Max(minimumSize * .5f, Mathf.Sqrt(radiusSquared) + 1e-6f);
            float volume = 4f / 3f * Mathf.PI * radius * radius * radius;
            if (volume < best.Volume)
                best = new Shape{Type = Kind.Sphere, Center = center, Radius = radius, Volume = volume};
            for (int i = 0; i < 3; i++)
            {
                Vector3 x = rotation * Vector3.right, y = rotation * Vector3.up, z = rotation * Vector3.forward;
                Vector3 axis = i == 0 ? x : i == 1 ? y : z;
                float radialSquared = 0;
                foreach (int id in ids)
                {
                    Vector3 d = vertices[id] - center;
                    radialSquared = Mathf.Max(radialSquared, (d - axis * Vector3.Dot(d, axis)).sqrMagnitude);
                }

                radius = Mathf.Max(minimumSize * .5f, Mathf.Sqrt(radialSquared) + 1e-6f);
                float lower = float.PositiveInfinity, upper = float.NegativeInfinity;
                foreach (int id in ids)
                {
                    Vector3 d = vertices[id] - center;
                    float t = Vector3.Dot(d, axis);
                    float radial = (d - axis * t).sqrMagnitude;
                    float cap = Mathf.Sqrt(Mathf.Max(0, radius * radius - radial));
                    lower = Mathf.Min(lower, t + cap);
                    upper = Mathf.Max(upper, t - cap);
                }

                float length = Mathf.Max(0, upper - lower);
                bool sphere = length <= radius * 1e-5f;
                if (sphere)
                {
                    radius += length * .5f;
                    length = 0;
                }

                volume = Mathf.PI * radius * radius * (length + 4f / 3f * radius);
                if (volume < best.Volume)
                    best = new Shape{Type = sphere ? Kind.Sphere : Kind.Capsule, Center = center + axis * ((lower + upper) * .5f), Rotation = i == 0 ? Rotation(y, x, -z) : i == 1 ? rotation : Rotation(x, z, -y), Radius = radius, Height = length + 2 * radius, Volume = volume};
            }

            return best;
        }

        private static Quaternion Frame(Quadric q)
        {
            var m = new[, ]{{q.XX, q.XY, q.XZ}, {q.XY, q.YY, q.YZ}, {q.XZ, q.YZ, q.ZZ}};
            var v = new double[3, 3];
            for (int i = 0; i < 3; i++)
                v[i, i] = 1;
            double magnitude = Math.Abs(q.XX) + Math.Abs(q.YY) + Math.Abs(q.ZZ);
            for (int step = 0; step < 24; step++)
            {
                int a = 0, b = 1;
                for (int i = 0; i < 3; i++)
                    for (int j = i + 1; j < 3; j++)
                        if (Math.Abs(m[i, j]) > Math.Abs(m[a, b]))
                        {
                            a = i;
                            b = j;
                        }

                if (Math.Abs(m[a, b]) <= magnitude * 1e-12)
                    break;
                double angle = .5 * Math.Atan2(2 * m[a, b], m[b, b] - m[a, a]);
                double c = Math.Cos(angle), s = Math.Sin(angle);
                for (int i = 0; i < 3; i++)
                {
                    double x = m[i, a], y = m[i, b];
                    m[i, a] = c * x - s * y;
                    m[i, b] = s * x + c * y;
                    x = v[i, a];
                    y = v[i, b];
                    v[i, a] = c * x - s * y;
                    v[i, b] = s * x + c * y;
                }

                for (int i = 0; i < 3; i++)
                {
                    double x = m[a, i], y = m[b, i];
                    m[a, i] = c * x - s * y;
                    m[b, i] = s * x + c * y;
                }
            }

            Vector3 Axis(int i) => new Vector3((float)v[0, i], (float)v[1, i], (float)v[2, i]).normalized;
            Vector3 first = Axis(0), second = Axis(1);
            return Rotation(first, second, Vector3.Cross(first, second).normalized);
        }

        // Matrix-to-quaternion conversion keeps the worker pure managed math (no Unity native calls).
        private static Quaternion Rotation(Vector3 x, Vector3 y, Vector3 z)
        {
            float a = x.x, b = y.y, c = z.z;
            Quaternion q;
            if (a + b + c > 0)
            {
                float s = Mathf.Sqrt(1 + a + b + c) * 2;
                q = new Quaternion((y.z - z.y) / s, (z.x - x.z) / s, (x.y - y.x) / s, s * .25f);
            }
            else if (a > b && a > c)
            {
                float s = Mathf.Sqrt(1 + a - b - c) * 2;
                q = new Quaternion(s * .25f, (y.x + x.y) / s, (z.x + x.z) / s, (y.z - z.y) / s);
            }
            else if (b > c)
            {
                float s = Mathf.Sqrt(1 + b - a - c) * 2;
                q = new Quaternion((y.x + x.y) / s, s * .25f, (z.y + y.z) / s, (z.x - x.z) / s);
            }
            else
            {
                float s = Mathf.Sqrt(1 + c - a - b) * 2;
                q = new Quaternion((z.x + x.z) / s, (z.y + y.z) / s, s * .25f, (x.y - y.x) / s);
            }

            float norm = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / norm, q.y / norm, q.z / norm, q.w / norm);
        }
    }
}
