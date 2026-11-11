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
        // A convex primitive contains a box iff it contains all eight corners, and contains a
        // capsule iff it contains its two endpoint balls. This is not a projected/AABB test.
        private static bool ContainsPrimitive(Shape outer, Shape inner)
        {
            if (inner.Type == Kind.Box)
            {
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            if (!ContainsBall(outer, inner.Center + inner.Rotation * Vector3.Scale(inner.Size * .5f, new Vector3(x, y, z)), 0))
                                return false;
                return true;
            }

            float half = inner.Type == Kind.Sphere ? 0 : Mathf.Max(0, inner.Height * .5f - inner.Radius);
            Vector3 offset = inner.Rotation * Vector3.up * half;
            return ContainsBall(outer, inner.Center - offset, inner.Radius) && ContainsBall(outer, inner.Center + offset, inner.Radius);
        }

        private static bool ContainsBall(Shape outer, Vector3 center, float radius)
        {
            var q = outer.Rotation;
            Vector3 p = new Quaternion(-q.x, -q.y, -q.z, q.w) * (center - outer.Center);
            const float epsilon = 1e-7f; // Coordinates are normalized by the item's longest side.
            if (outer.Type == Kind.Box)
                return Mathf.Abs(p.x) + radius <= outer.Size.x * .5f + epsilon && Mathf.Abs(p.y) + radius <= outer.Size.y * .5f + epsilon && Mathf.Abs(p.z) + radius <= outer.Size.z * .5f + epsilon;
            float half = outer.Type == Kind.Sphere ? 0 : Mathf.Max(0, outer.Height * .5f - outer.Radius);
            p.y -= Mathf.Clamp(p.y, -half, half);
            return p.magnitude + radius <= outer.Radius + epsilon;
        }

        private static Shape[] RemoveContained(Shape[] shapes, CancellationToken token)
        {
            var kept = shapes.ToList();
            for (int i = kept.Count - 1; i >= 0; i--)
            {
                token.ThrowIfCancellationRequested();
                for (int j = 0; j < kept.Count; j++)
                {
                    if (i == j || !ContainsPrimitive(kept[j], kept[i]))
                        continue;
                    // Equal shapes retain the earlier one; a containing chain always retains its outermost member.
                    if (j > i && ContainsPrimitive(kept[i], kept[j]))
                        continue;
                    kept.RemoveAt(i);
                    break;
                }
            }

            return kept.ToArray();
        }

        // Uniform-in-volume deterministic samples. These measure 3D overlap, not silhouette overlap:
        // two disconnected parts can project onto the same pixel and still have zero intersection.
        private sealed class VolumeProbe
        {
            internal readonly Shape Shape;
            internal readonly Vector3 Low, High;
            internal readonly Vector3[] Samples;
            internal VolumeProbe(Shape shape)
            {
                Shape = shape;
                Vector3 extent;
                if (shape.Type == Kind.Box)
                {
                    Vector3 Abs(Vector3 p) => new Vector3(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z));
                    extent = Abs(shape.Rotation * Vector3.right) * shape.Size.x * .5f + Abs(shape.Rotation * Vector3.up) * shape.Size.y * .5f + Abs(shape.Rotation * Vector3.forward) * shape.Size.z * .5f;
                }
                else
                {
                    Vector3 line = shape.Rotation * Vector3.up * (shape.Type == Kind.Sphere ? 0 : Mathf.Max(0, shape.Height * .5f - shape.Radius));
                    extent = new Vector3(Mathf.Abs(line.x), Mathf.Abs(line.y), Mathf.Abs(line.z)) + Vector3.one * shape.Radius;
                }

                Low = shape.Center - extent;
                High = shape.Center + extent;
                Samples = new Vector3[128];
                Vector3 size = shape.Type == Kind.Box ? shape.Size : new Vector3(shape.Radius * 2, shape.Type == Kind.Sphere ? shape.Radius * 2 : shape.Height, shape.Radius * 2);
                int count = 0;
                for (int i = 1; count < Samples.Length; i++)
                {
                    Vector3 p = Vector3.Scale(size, new Vector3(Halton(i, 2) - .5f, Halton(i, 3) - .5f, Halton(i, 5) - .5f));
                    float half = shape.Type == Kind.Sphere ? 0 : Mathf.Max(0, shape.Height * .5f - shape.Radius);
                    Vector3 radial = p;
                    radial.y -= Mathf.Clamp(radial.y, -half, half);
                    if (shape.Type != Kind.Box && radial.sqrMagnitude > shape.Radius * shape.Radius)
                        continue;
                    Samples[count++] = shape.Center + shape.Rotation * p;
                }
            }

            private static float Halton(int index, int radix)
            {
                float fraction = 1, value = 0;
                while (index > 0)
                {
                    fraction /= radix;
                    value += fraction * (index % radix);
                    index /= radix;
                }

                return value;
            }
        }

        private static float PairOverlap(VolumeProbe a, VolumeProbe b)
        {
            if (a.High.x <= b.Low.x || b.High.x <= a.Low.x || a.High.y <= b.Low.y || b.High.y <= a.Low.y || a.High.z <= b.Low.z || b.High.z <= a.Low.z)
                return 0;
            if (ContainsPrimitive(a.Shape, b.Shape) || ContainsPrimitive(b.Shape, a.Shape))
                return 1;
            if (a.Shape.Volume > b.Shape.Volume)
            {
                var swap = a;
                a = b;
                b = swap;
            }

            int inside = 0;
            foreach (var p in a.Samples)
                if (ContainsBall(b.Shape, p, 0))
                    inside++;
            return (float)inside / a.Samples.Length;
        }

        private static float OverlapPenalty(VolumeProbe a, IEnumerable<VolumeProbe> others)
        {
            float penalty = 0;
            foreach (var b in others)
            {
                float excess = Mathf.Max(0, PairOverlap(a, b) - .15f); // A small joining intersection is useful.
                penalty += excess * excess;
            }

            return penalty * .25f;
        }

        private static float TotalOverlapPenalty(IEnumerable<Shape> shapes, CandidateCache cache)
        {
            var probes = shapes.Select(s => cache.Probe(s)).ToArray();
            float penalty = 0;
            for (int i = 0; i < probes.Length; i++)
                penalty += OverlapPenalty(probes[i], probes.Skip(i + 1));
            return penalty;
        }

        private static Shape[] SuppressOverlap(Shape[] shapes, Silhouettes silhouette, CandidateCache cache, float budget, float minimum, ColliderFitProgress progress, CancellationToken token)
        {
            shapes = RemoveContained(shapes, token);
            var masks = shapes.Select(s => cache.Mask(s)).ToArray();
            progress?.Report(0, "清理三维重叠与包含形状");
            var steps = new[]{.2f, .1f, .05f, .025f};
            for (int stepIndex = 0; stepIndex < steps.Length; stepIndex++)
            {
                float step = steps[stepIndex];
                for (int pass = 0; pass < 3; pass++)
                {
                    bool changed = false;
                    for (int i = 0; i < shapes.Length; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        progress?.Report((stepIndex * 3 + pass + (float)i / shapes.Length) / (steps.Length * 3),
                            $"清理三维重叠：第 {stepIndex + 1}/{steps.Length} 档，第 {pass + 1}/3 轮，形状 {i + 1}/{shapes.Length}");
                        var others = new ulong[silhouette.WordCount];
                        for (int j = 0; j < masks.Length; j++)
                            if (j != i)
                                for (int k = 0; k < others.Length; k++)
                                    others[k] |= masks[j][k];
                        var probes = shapes.Where((s, j) => j != i).Select(s => cache.Probe(s)).ToArray();
                        float error = silhouette.Error(others, masks[i]);
                        float penalty = OverlapPenalty(cache.Probe(shapes[i]), probes);
                        if (penalty == 0)
                            continue;
                        float allowedError = Mathf.Max(budget, error);
                        float bestScore = error + penalty;
                        Shape best = shapes[i];
                        ulong[] bestMask = masks[i];
                        foreach (var candidate in Adjustments(shapes[i], step, minimum).Concat(EndTrims(shapes[i], step, minimum)))
                        {
                            token.ThrowIfCancellationRequested();
                            var mask = cache.Mask(candidate);
                            float nextError = silhouette.Error(others, mask);
                            if (nextError > allowedError + 1e-6f)
                                continue;
                            float score = nextError + OverlapPenalty(cache.Probe(candidate), probes);
                            if (score >= bestScore - 1e-5f)
                                continue;
                            best = candidate;
                            bestMask = mask;
                            bestScore = score;
                        }

                        if (ReferenceEquals(best, shapes[i]))
                            continue;
                        shapes[i] = best;
                        masks[i] = bestMask;
                        changed = true;
                    }

                    if (!changed)
                        break;
                }
            }

            shapes = RemoveContained(shapes, token);
            progress?.Report(1, "重叠清理完成");
            return shapes;
        }

        private static IEnumerable<Shape> EndTrims(Shape shape, float fraction, float minimum)
        {
            // Tapered meshes keep their parameterized ends; only boxes and capsules are trimmed.
            if (shape.Type == Kind.Sphere)
                yield break;
            for (int axis = 0; axis < 3; axis++)
            {
                if (shape.Type == Kind.Capsule && axis != 1)
                    continue;
                float length = shape.Type == Kind.Box ? shape.Size[axis] : shape.Height;
                float removed = length * fraction;
                if (length - removed < (shape.Type == Kind.Box ? minimum : 2 * shape.Radius))
                    continue;
                foreach (float sign in new[]{-1f, 1f})
                {
                    Vector3 direction = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                    var trimmed = new Shape{Type = shape.Type, Rotation = shape.Rotation, Center = shape.Center + shape.Rotation * direction * (removed * .5f * sign), Size = shape.Size, Radius = shape.Radius, Height = shape.Height};
                    if (shape.Type == Kind.Box)
                    {
                        Vector3 size = trimmed.Size;
                        size[axis] -= removed;
                        trimmed.Size = size;
                        trimmed.Volume = size.x * size.y * size.z;
                    }
                    else
                    {
                        trimmed.Height -= removed;
                        trimmed.Volume = Mathf.PI * trimmed.Radius * trimmed.Radius * (trimmed.Height - 2f / 3 * trimmed.Radius);
                    }

                    yield return trimmed;
                }
            }
        }
    }
}
