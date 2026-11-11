using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Shape = July.Physics.Authoring.PrimitiveColliderFitter.Shape;
using Kind = July.Physics.Authoring.PrimitiveColliderFitter.Kind;

namespace July.Physics.Authoring
{
    internal static partial class ColliderDecomposition
    {
        private sealed class SpatialTrial
        {
            internal int Index;
            internal Region Left, Right;
            internal Shape[] Others;
            internal ColliderGpuScoring.CandidatePair Input;
        }

        // Practical extension of the CPD starting partition, not part of the paper's enclosing fit.
        // Compare the union for contour quality; a separate 3D pass removes nesting and penalizes overlap.
        private static Shape[] Simplify(Vector3[] points, int[] triangles, float[] weights, Region[] regions, ColliderGenerationSettings settings, Result result, ColliderFitProgress progress, CancellationToken token, ColliderGpuScoring scoring)
        {
            var silhouette = new Silhouettes(points, triangles, weights, token, settings.Goal == ColliderGeometryGoal.Envelope,
                progress?.Slice(0, .08f));
            if (silhouette.WordCount == 0)
            {
                progress?.Report(1, "无有效轮廓面积，主体拟合完成");
                return regions.Select(r => r.Shape).ToArray();
            }

            var cache = new CandidateCache(silhouette);
            var parts = regions.ToList();
            var masks = parts.Select(r => cache.Mask(r.Shape)).ToList();
            float budget = settings.SilhouetteErrorPercent * .01f;
            float trim = settings.DetailAreaPercent * .01f;
            float minimum = settings.MinimumThicknessPercent * .01f;
            ulong[] Except(int a, int b = -1)
            {
                var mask = new ulong[silhouette.WordCount];
                for (int i = 0; i < masks.Count; i++)
                    if (i != a && i != b)
                        for (int j = 0; j < mask.Length; j++)
                            mask[j] |= masks[i][j];
                return mask;
            }

            Shape[] Candidates(Region region)
            {
                if (region.Candidates != null) return region.Candidates;
                var candidates = new List<Shape>();
                foreach (var candidate in SoftCandidates(points, region.Vertices, weights, region.Q, minimum, trim))
                {
                    token.ThrowIfCancellationRequested();
                    candidates.Add(candidate);
                }
                return region.Candidates = candidates.ToArray();
            }

            Shape[] OtherShapes(int a, int b = -1) => parts.Where((r, i) => i != a && i != b).Select(r => r.Shape).ToArray();
            (Shape shape, ulong[] mask, float error) Best(Region region, ulong[] others, Shape[] otherShapes = null)
            {
                var job = Contour(region.Shape, others, otherShapes ?? Array.Empty<Shape>(), silhouette, cache, budget);
                SelectContours(new[] { job }, new[] { Candidates(region) }, silhouette, cache, null, token);
                return (job.Shape, cache.Mask(job.Shape), job.Error);
            }

            (Shape shape, ulong[] mask) Refine(Shape shape, ulong[] others, Shape[] otherShapes = null)
            {
                var job = Contour(shape, others, otherShapes ?? Array.Empty<Shape>(), silhouette, cache, budget);
                RefineContours(new[] { job }, silhouette, cache, budget, minimum, null, token);
                return (job.Shape, cache.Mask(job.Shape));
            }

            Region Combined(int a, int b)
            {
                var ids = Union(parts[a].Vertices, parts[b].Vertices);
                var q = parts[a].Q + parts[b].Q;
                return new Region{Vertices = ids, Q = q, Shape = FitRegion(points, ids, q, minimum)};
            }

            // A global candidate avoids a local merge barrier: a sphere should remain one sphere at budget 8.
            var whole = new Region{Vertices = Enumerable.Range(0, points.Length).Where(i => weights[i] > 0).ToArray(), Q = regions.Aggregate(default(Quadric), (q, r) => q + r.Q)};
            whole.Shape = FitRegion(points, whole.Vertices, whole.Q, minimum);
            progress?.Report(.08f, "允许误差拟合：比较主体与六方向轮廓");
            var single = Best(whole, new ulong[silhouette.WordCount]);
            if (single.error <= budget)
            {
                whole.Shape = single.shape;
                parts.Clear();
                parts.Add(whole);
                masks.Clear();
                masks.Add(single.mask);
            }
            else
            {
                // Refit each region against the other shapes, including holes and missing body outline.
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < parts.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        progress?.Report(.15f + .15f * (pass * parts.Count + i) / (2 * parts.Count),
                            $"区域轮廓拟合：{pass * parts.Count + i}/{2 * parts.Count}");
                        var fit = Best(parts[i], Except(i), OtherShapes(i));
                        parts[i].Shape = fit.shape;
                        masks[i] = fit.mask;
                    }

                // Face-volume merging can leave surface slivers that no final pair merge can repair.
                // Compare a spatial split seed too: spend the same count ceiling on body sections.
                var cpdParts = parts.ToArray();
                var cpdMasks = masks.ToArray();
                float cpdError = silhouette.Error(Except(-1));
                float cpdScore = cpdError + TotalOverlapPenalty(parts.Select(r => r.Shape), cache);
                whole.Shape = single.shape;
                parts.Clear();
                parts.Add(whole);
                masks.Clear();
                masks.Add(single.mask);
                while (parts.Count < settings.TargetCount && silhouette.Error(Except(-1)) > budget)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(.3f + .22f * (parts.Count - 1) / Math.Max(1, settings.TargetCount - 1),
                        $"主体分段拟合：{parts.Count}/{settings.TargetCount}");
                    // Above the contour budget, a split must improve the body even if it temporarily
                    // increases overlap. Among those improving splits prefer the least redundant fit.
                    float allowedError = silhouette.Error(Except(-1)) - 1e-5f;
                    float bestScore = float.PositiveInfinity;
                    int bestIndex = -1;
                    Region bestLeft = null, bestRight = null;
                    ulong[] bestLeftMask = null, bestRightMask = null;
                    int trial = 0, trialWork = parts.Count * 9;
                    var trials = new List<SpatialTrial>();
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var source = parts[i];
                        var others = Except(i);
                        var otherShapes = OtherShapes(i);
                        Quaternion frame = PointFrame(points, source.Vertices, weights);
                        for (int axis = 0; axis < 3; axis++)
                        {
                            Vector3 direction = frame * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
                            float low = source.Vertices.Min(id => Vector3.Dot(points[id], direction));
                            float high = source.Vertices.Max(id => Vector3.Dot(points[id], direction));
                            foreach (float fraction in new[]{1f / 3, .5f, 2f / 3})
                            {
                                token.ThrowIfCancellationRequested();
                                int done = trial++;
                                progress?.Report(.3f + .22f * (parts.Count - 1 + (float)done / trialWork) / Math.Max(1, settings.TargetCount - 1),
                                    $"主体分段拟合：{parts.Count}/{settings.TargetCount}；比较候选 {trial}/{trialWork}");
                                float plane = low + (high - low) * fraction;
                                var leftIds = source.Vertices.Where(id => Vector3.Dot(points[id], direction) <= plane).ToArray();
                                var rightIds = source.Vertices.Where(id => Vector3.Dot(points[id], direction) > plane).ToArray();
                                if (leftIds.Length < 3 || rightIds.Length < 3)
                                    continue;
                                var left = new Region{Vertices = leftIds, Q = source.Q};
                                var right = new Region{Vertices = rightIds, Q = source.Q};
                                left.Shape = FitRegion(points, leftIds, left.Q, minimum);
                                right.Shape = FitRegion(points, rightIds, right.Q, minimum);
                                trials.Add(new SpatialTrial { Index = i, Left = left, Right = right,
                                    Input = new ColliderGpuScoring.CandidatePair {
                                        Left = new[] { left.Shape }.Concat(Candidates(left)).ToArray(),
                                        Right = new[] { right.Shape }.Concat(Candidates(right)).ToArray(), Background = others },
                                    Others = otherShapes });
                            }
                        }
                    }

                    var tables = scoring?.Score(silhouette, trials.Select(t => t.Input).ToArray());
                    for (int t = 0; t < trials.Count; t++)
                    {
                        token.ThrowIfCancellationRequested();
                        var item = trials[t]; var left = item.Left; var right = item.Right;
                        var others = item.Input.Background;
                        ulong[] leftMask, rightMask;
                        if (tables != null)
                        {
                            var table = tables[t]; int l = 0, r = 0, columns = item.Input.Right.Length;
                            for (int pass = 0; pass < 2; pass++)
                            {
                                float value = table[l * columns + r];
                                for (int c = 1; c < item.Input.Left.Length; c++)
                                    if (table[c * columns + r] < value - 1e-6f) { l = c; value = table[l * columns + r]; }
                                value = table[l * columns + r];
                                for (int c = 1; c < columns; c++)
                                    if (table[l * columns + c] < value - 1e-6f) { r = c; value = table[l * columns + r]; }
                            }
                            left.Shape = item.Input.Left[l]; right.Shape = item.Input.Right[r];
                            leftMask = cache.Mask(left.Shape); rightMask = cache.Mask(right.Shape);
                        }
                        else
                        {
                            leftMask = cache.Mask(left.Shape); rightMask = cache.Mask(right.Shape);
                            for (int pass = 0; pass < 2; pass++)
                            {
                                var background = others.Select((word, j) => word | rightMask[j]).ToArray();
                                var fitLeft = Best(left, background); left.Shape = fitLeft.shape; leftMask = fitLeft.mask;
                                background = others.Select((word, j) => word | leftMask[j]).ToArray();
                                var fitRight = Best(right, background); right.Shape = fitRight.shape; rightMask = fitRight.mask;
                            }
                        }
                        float error = silhouette.Error(others.Select((word, j) => word | leftMask[j]).ToArray(), rightMask);
                        float score = error + TotalOverlapPenalty(item.Others.Concat(new[] { left.Shape, right.Shape }), cache);
                        if (error > allowedError + 1e-6f || score >= bestScore) continue;
                        bestScore = score; bestIndex = item.Index; bestLeft = left; bestRight = right;
                        bestLeftMask = leftMask; bestRightMask = rightMask;
                    }

                    if (bestIndex < 0)
                        break;
                    parts[bestIndex] = bestLeft;
                    masks[bestIndex] = bestLeftMask;
                    parts.Add(bestRight);
                    masks.Add(bestRightMask);
                }

                if (silhouette.Error(Except(-1)) + TotalOverlapPenalty(parts.Select(r => r.Shape), cache) > cpdScore)
                {
                    parts.Clear();
                    parts.AddRange(cpdParts);
                    masks.Clear();
                    masks.AddRange(cpdMasks);
                }

                // Permit actual local underfit, not just removal of outlying region vertices.
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < parts.Count; i++)
                    {
                        progress?.Report(.52f + .2f * (pass * parts.Count + i) / (2 * parts.Count),
                            $"微调轮廓：{pass * parts.Count + i}/{2 * parts.Count}");
                        var background = Except(i);
                        var otherShapes = OtherShapes(i);
                        var fit = Best(parts[i], background, otherShapes);
                        var refined = Refine(fit.shape, background, otherShapes);
                        parts[i].Shape = refined.shape;
                        masks[i] = refined.mask;
                    }

                int mergeStart = parts.Count;
                while (parts.Count > 1)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(.72f + .1f * (mergeStart - parts.Count) / Math.Max(1, mergeStart - 1),
                        $"轮廓容差简化：剩余 {parts.Count} 个碰撞体");
                    float current = silhouette.Error(Except(-1));
                    // Above budget, accept only actual improvements; never spend an additional budget per merge.
                    float allowed = Mathf.Max(budget, current);
                    int bestA = -1, bestB = -1;
                    Region replacement = null;
                    ulong[] replacementMask = null;
                    float bestScore = float.PositiveInfinity;
                    var merges = new List<(int a, int b, Region region, ContourJob job)>();
                    float roundStart = .72f + .1f * (mergeStart - parts.Count) / Math.Max(1, mergeStart - 1);
                    float roundSpan = .1f / Math.Max(1, mergeStart - 1);
                    for (int a = 0; a < parts.Count; a++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (int b = a + 1; b < parts.Count; b++)
                        {
                            var region = Combined(a, b);
                            merges.Add((a, b, region, Contour(region.Shape, Except(a, b), OtherShapes(a, b), silhouette, cache, budget)));
                        }
                        progress?.Report(roundStart + roundSpan * .15f * (a + 1f) / parts.Count,
                            $"轮廓容差简化：准备候选 {a + 1}/{parts.Count} 组");
                    }
                    var jobs = merges.Select(m => m.job).ToArray();
                    SelectContours(jobs, merges.Select(m => Candidates(m.region)).ToArray(), silhouette, cache, scoring, token);
                    RefineContours(jobs, silhouette, cache, budget, minimum, scoring, token,
                        progress?.Slice(roundStart + roundSpan * .25f, roundStart + roundSpan * .85f));
                    int trial = 0;
                    for (int a = 0; a < parts.Count; a++)
                    {
                        token.ThrowIfCancellationRequested();
                        float removed = silhouette.Error(Except(a));
                        float removedScore = removed + TotalOverlapPenalty(OtherShapes(a), cache);
                        if (removed <= allowed && removedScore < bestScore)
                        {
                            bestA = a; bestB = -1; replacement = null; replacementMask = null; bestScore = removedScore;
                        }
                        for (int b = a + 1; b < parts.Count; b++)
                        {
                            var item = merges[trial++];
                            var mask = cache.Mask(item.job.Shape);
                            float error = silhouette.Error(item.job.Background, mask);
                            float score = error + TotalOverlapPenalty(OtherShapes(a, b).Concat(new[] { item.job.Shape }), cache);
                            if (error <= allowed && score < bestScore)
                            {
                                item.region.Shape = item.job.Shape; replacement = item.region; replacementMask = mask;
                                bestA = a; bestB = b; bestScore = score;
                            }
                        }
                        progress?.Report(roundStart + roundSpan * (.85f + .15f * (a + 1f) / parts.Count),
                            $"轮廓容差简化：已比较 {a + 1}/{parts.Count} 组");
                    }

                    if (bestA < 0)
                        break;
                    if (replacement == null)
                    {
                        parts.RemoveAt(bestA);
                        masks.RemoveAt(bestA);
                    }
                    else
                    {
                        parts[bestA] = replacement;
                        masks[bestA] = replacementMask;
                        parts.RemoveAt(bestB);
                        masks.RemoveAt(bestB);
                    }
                }
            }

            var shapes = SuppressOverlap(parts.Select(r => r.Shape).ToArray(), silhouette, cache, budget, minimum,
                progress?.Slice(.82f, .98f), token);
            progress?.Report(.98f, "测量最终轮廓");
            var final = new ulong[silhouette.WordCount];
            foreach (var shape in shapes)
            {
                var mask = cache.Mask(shape);
                for (int i = 0; i < final.Length; i++)
                    final[i] |= mask[i];
            }

            silhouette.Measure(final, out float missing, out float extra);
            result.MissingPercent = missing * 100;
            result.ExtraPercent = extra * 100;
            progress?.Report(1, "轮廓拟合完成");
            return shapes;
        }

        // Shapes stay immutable during contour fitting. World rescaling happens
        // after this per-call cache has gone out of scope. Bound trial retention:
        // 256 entries use at most about 1.5 MiB of mask/sample payload at 6x72x72.
        private sealed class CandidateCache
        {
            const int Capacity = 256;
            sealed class Entry { internal ulong[] Mask; internal VolumeProbe Probe; }
            readonly Silhouettes silhouette;
            readonly Dictionary<Shape, Entry> entries = new Dictionary<Shape, Entry>();

            internal CandidateCache(Silhouettes silhouette) { this.silhouette = silhouette; }
            Entry Get(Shape shape)
            {
                if (entries.TryGetValue(shape, out var entry)) return entry;
                if (entries.Count == Capacity) entries.Clear();
                entry = new Entry();
                entries.Add(shape, entry);
                return entry;
            }

            internal ulong[] Mask(Shape shape)
            {
                var entry = Get(shape);
                return entry.Mask ?? (entry.Mask = silhouette.Render(shape));
            }

            internal VolumeProbe Probe(Shape shape)
            {
                var entry = Get(shape);
                return entry.Probe ?? (entry.Probe = new VolumeProbe(shape));
            }
        }

        private static IEnumerable<Shape> Adjustments(Shape shape, float step, float minimum)
        {
            Shape Copy() => new Shape{Type = shape.Type, Center = shape.Center, Rotation = shape.Rotation, Size = shape.Size, Radius = shape.Radius, Height = shape.Height, Volume = shape.Volume};
            Shape Volume(Shape s)
            {
                s.Volume = s.Type == Kind.Box ? s.Size.x * s.Size.y * s.Size.z : Mathf.PI * s.Radius * s.Radius * (Mathf.Max(0, s.Height - 2 * s.Radius) + 4f / 3 * s.Radius);
                if (s.Type == Kind.Sphere)
                    s.Volume = 4f / 3 * Mathf.PI * s.Radius * s.Radius * s.Radius;
                return s;
            }

            for (int axis = 0; axis < 3; axis++)
                foreach (float sign in new[]{-1f, 1f})
                {
                    Vector3 direction = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                    var shifted = Copy();
                    float extent = shape.Type == Kind.Box ? shape.Size[axis] : axis == 1 && shape.Type == Kind.Capsule ? shape.Height : shape.Radius * 2;
                    shifted.Center += shape.Rotation * direction * (extent * step * sign * .5f);
                    yield return shifted;
                    if (shape.Type != Kind.Box)
                        continue;
                    var resized = Copy();
                    var size = resized.Size;
                    size[axis] = Mathf.Max(minimum, size[axis] * (1 + sign * step));
                    resized.Size = size;
                    yield return Volume(resized);
                }

            if (shape.Type == Kind.Box)
                yield break;
            foreach (float sign in new[]{-1f, 1f})
            {
                var resized = Copy();
                resized.Radius = Mathf.Max(minimum * .5f, shape.Radius * (1 + sign * step));
                resized.Height = Mathf.Max(0, shape.Height - 2 * shape.Radius) + 2 * resized.Radius;
                yield return Volume(resized);
                if (shape.Type != Kind.Capsule)
                    continue;
                resized = Copy();
                resized.Height = (shape.Height - 2 * shape.Radius) * (1 + sign * step) + 2 * shape.Radius;
                yield return Volume(resized);
            }
        }

        private static Quaternion PointFrame(Vector3[] points, int[] ids, float[] weights)
        {
            float total = ids.Sum(i => weights[i]);
            Vector3 center = Vector3.zero;
            foreach (int i in ids)
                center += points[i] * (weights[i] / total);
            Quadric covariance = default;
            foreach (int i in ids)
                covariance += Quadric.Outer(points[i] - center, weights[i]);
            return Frame(covariance);
        }

        // Area weights, rather than vertex counts, keep dense tessellation and tiny decorations from
        // dominating bounds. Trimming supplies candidates; the global silhouette budget decides acceptance.
        private static IEnumerable<Shape> SoftCandidates(Vector3[] points, int[] ids, float[] weights, Quadric normals, float minimum, float trim)
        {
            float total = ids.Sum(i => weights[i]);
            Vector3 centroid = Vector3.zero;
            foreach (int i in ids)
                centroid += points[i] * (weights[i] / total);
            Quadric covariance = default;
            foreach (int i in ids)
                covariance += Quadric.Outer(points[i] - centroid, weights[i]);
            float Quantile(Func<int, float> value, float fraction)
            {
                var sorted = ids.Select(i => (value: value(i), weight: weights[i])).OrderBy(p => p.value);
                float accumulated = 0, last = 0;
                foreach (var p in sorted)
                {
                    last = p.value;
                    accumulated += p.weight;
                    if (accumulated >= total * fraction)
                        return last;
                }

                return last;
            }

            foreach (var rotation in new[]{Frame(normals), Frame(covariance)})
            {
                var inverse = new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w);
                Vector3 low = Vector3.zero, high = Vector3.zero;
                for (int axis = 0; axis < 3; axis++)
                {
                    int k = axis;
                    low[k] = Quantile(i => (inverse * points[i])[k], trim * .5f);
                    high[k] = Quantile(i => (inverse * points[i])[k], 1 - trim * .5f);
                }

                Vector3 center = rotation * ((low + high) * .5f);
                Vector3 size = Vector3.Max(high - low, Vector3.one * minimum);
                yield return new Shape{Type = Kind.Box, Center = center, Rotation = rotation, Size = size, Volume = size.x * size.y * size.z};
                float radius = Mathf.Max(minimum * .5f, Quantile(i => (points[i] - center).magnitude, 1 - trim));
                yield return new Shape{Type = Kind.Sphere, Center = center, Radius = radius, Volume = 4f / 3 * Mathf.PI * radius * radius * radius};
                for (int axisIndex = 0; axisIndex < 3; axisIndex++)
                {
                    Vector3 x = rotation * Vector3.right, y = rotation * Vector3.up, z = rotation * Vector3.forward;
                    Vector3 axis = axisIndex == 0 ? x : axisIndex == 1 ? y : z;
                    float Radial(int i)
                    {
                        Vector3 d = points[i] - center;
                        return (d - axis * Vector3.Dot(d, axis)).magnitude;
                    }

                    radius = Mathf.Max(minimum * .5f, Quantile(Radial, 1 - trim));
                    float End(int i, float sign)
                    {
                        float radial = Radial(i);
                        return Vector3.Dot(points[i] - center, axis) + sign * Mathf.Sqrt(Mathf.Max(0, radius * radius - radial * radial));
                    }

                    float lower = Quantile(i => End(i, 1), trim), upper = Quantile(i => End(i, -1), 1 - trim);
                    float length = Mathf.Max(0, upper - lower);
                    bool sphere = length <= radius * 1e-5f;
                    yield return new Shape{Type = sphere ? Kind.Sphere : Kind.Capsule, Center = center + axis * ((lower + upper) * .5f), Rotation = axisIndex == 0 ? Rotation(y, x, -z) : axisIndex == 1 ? rotation : Rotation(x, z, -y), Radius = radius, Height = length + 2 * radius, Volume = Mathf.PI * radius * radius * (length + 4f / 3 * radius)};
                }
            }
        }

        internal sealed class Silhouettes
        {
            internal const int Resolution = 72, Words = Resolution * Resolution / 64;
            internal sealed class View
            {
                internal Vector3 U, V;
                internal Vector2 Low, Step;
                internal int Offset, Pixels;
            }

            internal readonly List<View> Views = new List<View>();
            internal readonly ulong[] Reference;
            internal int WordCount => Views.Count * Words;
            internal Silhouettes(Vector3[] points, int[] triangles, float[] weights, CancellationToken token, bool envelope = false,
                ColliderFitProgress progress = null)
            {
                float total = weights.Sum();
                Vector3 center = Vector3.zero;
                for (int i = 0; i < points.Length; i++)
                    center += points[i] * (weights[i] / total);
                Quadric covariance = default;
                for (int i = 0; i < points.Length; i++)
                    covariance += Quadric.Outer(points[i] - center, weights[i]);
                var frame = Frame(covariance);
                var masks = new List<ulong[]>();
                int directionIndex = 0;
                foreach (var direction in new[]{Vector3.right, Vector3.up, Vector3.forward, new Vector3(1, 1, 1).normalized, new Vector3(1, -1, 1).normalized, new Vector3(-1, 1, 1).normalized})
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(directionIndex / 6f, $"构建轮廓视图：{++directionIndex}/6");
                    Vector3 normal = frame * direction;
                    Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right).normalized;
                    Vector3 v = Vector3.Cross(normal, u);
                    var projected = points.Select(p => new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v))).ToArray();
                    Vector2 low = projected[0], high = low;
                    foreach (var p in projected)
                    {
                        low = Vector2.Min(low, p);
                        high = Vector2.Max(high, p);
                    }

                    if (Mathf.Min(high.x - low.x, high.y - low.y) < .001f)
                        continue; // Edge-on open sheets have no area.
                    Vector2 margin = Vector2.one * .15f;
                    var view = new View{U = u, V = v, Low = low - margin, Step = (high - low + margin * 2) / Resolution, Offset = Views.Count * Words};
                    var mask = new ulong[Words];
                    if (envelope)
                        Polygon(view, Hull(projected.ToList()), mask, 0);
                    else
                        for (int i = 0; i < triangles.Length; i += 3)
                            Polygon(view, new[]{projected[triangles[i]], projected[triangles[i + 1]], projected[triangles[i + 2]]}, mask, 0);
                    view.Pixels = mask.Sum(word => Bits(word));
                    if (view.Pixels == 0)
                        continue;
                    Views.Add(view);
                    masks.Add(mask);
                }

                Reference = new ulong[WordCount];
                for (int i = 0; i < masks.Count; i++)
                    Array.Copy(masks[i], 0, Reference, i * Words, Words);
                progress?.Report(1, "六向轮廓读取完成");
            }

            internal ulong[] Render(Shape shape)
            {
                var mask = new ulong[WordCount];
                foreach (var view in Views)
                {
                    Vector2 Project(Vector3 p) => new Vector2(Vector3.Dot(p, view.U), Vector3.Dot(p, view.V));
                    if (shape.Type == Kind.Box)
                    {
                        var corners = new List<Vector2>();
                        for (int x = -1; x <= 1; x += 2)
                            for (int y = -1; y <= 1; y += 2)
                                for (int z = -1; z <= 1; z += 2)
                                    corners.Add(Project(shape.Center + shape.Rotation * Vector3.Scale(shape.Size * .5f, new Vector3(x, y, z))));
                        Polygon(view, Hull(corners), mask, view.Offset);
                    }
                    else
                    {
                        Vector3 axis = shape.Rotation * Vector3.up;
                        float half = shape.Type == Kind.Sphere ? 0 : Mathf.Max(0, shape.Height * .5f - shape.Radius);
                        Vector2 a = Project(shape.Center - axis * half), b = Project(shape.Center + axis * half);
                        Vector2 segment = b - a;
                        float squared = segment.sqrMagnitude, radiusSquared = shape.Radius * shape.Radius;
                        Raster(view, Vector2.Min(a, b) - Vector2.one * shape.Radius, Vector2.Max(a, b) + Vector2.one * shape.Radius, p =>
                        {
                            float t = squared == 0 ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, segment) / squared);
                            return (p - a - segment * t).sqrMagnitude <= radiusSquared;
                        }, mask, view.Offset);
                    }
                }

                return mask;
            }

            internal float Error(ulong[] mask, ulong[] added = null)
            {
                float error = 0;
                foreach (var view in Views)
                {
                    int changed = 0;
                    for (int j = view.Offset; j < view.Offset + Words; j++)
                        changed += Bits((mask[j] | (added == null ? 0 : added[j])) ^ Reference[j]);
                    error += (float)changed / view.Pixels;
                }

                return error / Views.Count;
            }

            internal void Measure(ulong[] mask, out float missing, out float extra)
            {
                missing = extra = 0;
                foreach (var view in Views)
                {
                    int under = 0, over = 0;
                    for (int j = view.Offset; j < view.Offset + Words; j++)
                    {
                        under += Bits(Reference[j] & ~mask[j]);
                        over += Bits(mask[j] & ~Reference[j]);
                    }

                    missing += (float)under / view.Pixels;
                    extra += (float)over / view.Pixels;
                }

                missing /= Views.Count;
                extra /= Views.Count;
            }

            private static int Bits(ulong x)
            {
                x -= (x >> 1) & 0x5555555555555555UL;
                x = (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
                return (int)((((x + (x >> 4)) & 0x0F0F0F0F0F0F0F0FUL) * 0x0101010101010101UL) >> 56);
            }

            private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
            internal static Vector2[] Hull(List<Vector2> points)
            {
                var sorted = points.OrderBy(p => p.x).ThenBy(p => p.y).Distinct().ToArray();
                var hull = new List<Vector2>();
                foreach (var p in sorted)
                {
                    while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }

                int lower = hull.Count;
                for (int i = sorted.Length - 2; i >= 0; i--)
                {
                    var p = sorted[i];
                    while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }

                if (hull.Count > 1)
                    hull.RemoveAt(hull.Count - 1);
                return hull.ToArray();
            }

            private static void Polygon(View view, Vector2[] polygon, ulong[] mask, int offset)
            {
                if (polygon.Length < 3)
                    return;
                float area = 0;
                Vector2 low = polygon[0], high = low;
                for (int i = 0; i < polygon.Length; i++)
                {
                    area += Cross(polygon[i], polygon[(i + 1) % polygon.Length]);
                    low = Vector2.Min(low, polygon[i]);
                    high = Vector2.Max(high, polygon[i]);
                }

                if (Mathf.Abs(area) < 1e-12f)
                    return;
                float sign = area > 0 ? 1 : -1;
                Raster(view, low, high, p =>
                {
                    for (int i = 0; i < polygon.Length; i++)
                        if (sign * Cross(polygon[(i + 1) % polygon.Length] - polygon[i], p - polygon[i]) < -1e-7f)
                            return false;
                    return true;
                }, mask, offset);
            }

            private static void Raster(View view, Vector2 low, Vector2 high, Func<Vector2, bool> inside, ulong[] mask, int offset)
            {
                int x0 = Mathf.Clamp((int)Math.Floor((low.x - view.Low.x) / view.Step.x), 0, Resolution - 1);
                int y0 = Mathf.Clamp((int)Math.Floor((low.y - view.Low.y) / view.Step.y), 0, Resolution - 1);
                int x1 = Mathf.Clamp((int)Math.Floor((high.x - view.Low.x) / view.Step.x), 0, Resolution - 1);
                int y1 = Mathf.Clamp((int)Math.Floor((high.y - view.Low.y) / view.Step.y), 0, Resolution - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (inside(view.Low + Vector2.Scale(view.Step, new Vector2(x + .5f, y + .5f))))
                        {
                            int bit = y * Resolution + x;
                            mask[offset + bit / 64] |= 1UL << (bit % 64);
                        }
            }
        }
    }
}
