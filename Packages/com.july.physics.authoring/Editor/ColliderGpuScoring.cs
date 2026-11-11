using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Context = July.Physics.Authoring.ColliderDecomposition.Silhouettes;
using Shape = July.Physics.Authoring.PrimitiveColliderFitter.Shape;
using Kind = July.Physics.Authoring.PrimitiveColliderFitter.Kind;

namespace July.Physics.Authoring
{
    // Owned by one PreviewAsync call. Only existing candidate silhouette scores use the GPU;
    // CPU selection order, overlap scoring and final geometry checks remain authoritative.
    internal sealed class ColliderGpuScoring : IDisposable
    {
        internal sealed class CandidatePair
        {
            internal Shape[] Left, Right;
            internal ulong[] Background;
        }

        [StructLayout(LayoutKind.Sequential)] private struct Projection
        {
            public Vector4 bounds, shape, segment, parameters, lowStep;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Pair { public int left, right, background; }
        private sealed class Batch
        {
            internal Projection[] Projections;
            internal Vector2[] PolygonPoints;
            internal Pair[] Pairs;
            internal uint[] Backgrounds, Reference;
            internal int Views, Shapes, Words;
        }

        // Bounds buffer memory and D3D11's 65,535 dispatch-group limit even at count 128.
        private const int TrialsPerBatch = 128;
        private readonly SynchronizationContext mainThread;
        private readonly CancellationToken token;
        private ComputeShader shader;
        private ComputeBuffer reference;
        internal int DispatchBatches { get; private set; }

        private ColliderGpuScoring(CancellationToken token)
        {
            this.token = token;
            mainThread = SynchronizationContext.Current;
        }

        // Unity 2022 can report compute support even with its headless Null Device.
        internal static ColliderGpuScoring TryCreate(CancellationToken token) =>
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && SystemInfo.supportsComputeShaders
                ? new ColliderGpuScoring(token) : null;

        internal float[][] Score(Context context, CandidatePair[] trials)
        {
            var result = new float[trials.Length][];
            for (int start = 0; start < trials.Length; start += TrialsPerBatch)
            {
                token.ThrowIfCancellationRequested();
                var batch = Prepare(context, trials, start, Math.Min(TrialsPerBatch, trials.Length - start));
                var counts = Submit(() => Execute(batch));
                token.ThrowIfCancellationRequested();
                int task = 0;
                for (int t = start; t < Math.Min(start + TrialsPerBatch, trials.Length); t++)
                {
                    var trial = trials[t];
                    int count = trial.Left.Length * trial.Right.Length;
                    result[t] = Normalize(context, counts, task, count);
                    task += count;
                }
            }
            return result;
        }

        internal float[][] ScoreCandidates(Context context, Shape[][] candidates, ulong[][] backgrounds)
        {
            var result = new float[candidates.Length][];
            for (int start = 0; start < candidates.Length; start += TrialsPerBatch)
            {
                token.ThrowIfCancellationRequested();
                int end = Math.Min(start + TrialsPerBatch, candidates.Length);
                var shapes = new List<Shape>(); var pairs = new List<Pair>(); var masks = new List<ulong[]>();
                for (int t = start; t < end; t++)
                {
                    masks.Add(backgrounds[t]);
                    foreach (var shape in candidates[t])
                    {
                        pairs.Add(new Pair { left = shapes.Count, right = shapes.Count, background = t - start });
                        shapes.Add(shape);
                    }
                }
                var batch = CreateBatch(context, shapes, pairs, masks);
                var counts = Submit(() => Execute(batch));
                token.ThrowIfCancellationRequested();
                int task = 0;
                for (int t = start; t < end; t++)
                {
                    result[t] = Normalize(context, counts, task, candidates[t].Length);
                    task += candidates[t].Length;
                }
            }
            return result;
        }

        // Keep CPU normalization in one place and in the original observation order.
        private static float[] Normalize(Context context, uint[] counts, int offset, int count)
        {
            var result = new float[count];
            for (int p = 0; p < count; p++)
            {
                float sum = 0;
                for (int v = 0; v < context.Views.Count; v++)
                    sum += (float)counts[(offset + p) * context.Views.Count + v] / context.Views[v].Pixels;
                result[p] = sum / context.Views.Count;
            }
            return result;
        }

        private T Submit<T>(Func<T> action)
        {
            var completion = new TaskCompletionSource<T>();
            mainThread.Post(_ =>
            {
                // Propagate driver, shader and cancellation errors to the fitting task.
                try { token.ThrowIfCancellationRequested(); completion.SetResult(action()); }
                catch (Exception e) { completion.SetException(e); }
            }, null);
            return completion.Task.GetAwaiter().GetResult();
        }

        private Batch Prepare(Context context, CandidatePair[] trials, int start, int count)
        {
            var shapes = new List<Shape>();
            var pairs = new List<Pair>();
            var backgrounds = new List<ulong[]>();
            for (int t = 0; t < count; t++)
            {
                token.ThrowIfCancellationRequested();
                var trial = trials[start + t];
                int left = shapes.Count; shapes.AddRange(trial.Left);
                int right = shapes.Count; shapes.AddRange(trial.Right);
                backgrounds.Add(trial.Background);
                for (int l = 0; l < trial.Left.Length; l++)
                    for (int r = 0; r < trial.Right.Length; r++)
                        pairs.Add(new Pair { left = left + l, right = right + r, background = t });
            }
            return CreateBatch(context, shapes, pairs, backgrounds);
        }

        private static Batch CreateBatch(Context context, List<Shape> shapes, List<Pair> pairs, List<ulong[]> backgrounds)
        {
            var polygons = new List<Vector2>();
            var projections = shapes.SelectMany(s => context.Views.Select(v => Project(s, v, polygons))).ToArray();
            return new Batch
            {
                Projections = projections, PolygonPoints = polygons.ToArray(),
                Pairs = pairs.ToArray(), Backgrounds = Words(backgrounds), Reference = Words(new[] { context.Reference }),
                Views = context.Views.Count, Shapes = shapes.Count, Words = context.WordCount * 2
            };
        }

        private uint[] Execute(Batch batch)
        {
            if (shader == null)
            {
                shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.july.physics.authoring/Editor/ColliderCandidateScores.compute"));
                shader.hideFlags = HideFlags.HideAndDontSave;
            }
            var buffers = new List<ComputeBuffer>();
            ComputeBuffer Buffer(int count, int stride) { var buffer = new ComputeBuffer(count, stride); buffers.Add(buffer); return buffer; }
            try
            {
                if (reference == null)
                {
                    reference = new ComputeBuffer(batch.Reference.Length, 4);
                    reference.SetData(batch.Reference);
                }
                var projections = Buffer(batch.Projections.Length, Marshal.SizeOf<Projection>()); projections.SetData(batch.Projections);
                // All-round batches have no polygon points; Unity requires non-empty buffers.
                var polygons = Buffer(Math.Max(1, batch.PolygonPoints.Length), 8);
                if (batch.PolygonPoints.Length > 0) polygons.SetData(batch.PolygonPoints);
                var backgrounds = Buffer(batch.Backgrounds.Length, 4); backgrounds.SetData(batch.Backgrounds);
                var pairs = Buffer(batch.Pairs.Length, 12); pairs.SetData(batch.Pairs);
                var masks = Buffer(batch.Shapes * batch.Words, 4);
                var counts = Buffer(batch.Pairs.Length * batch.Views, 4);
                int render = shader.FindKernel("RenderMasks"), score = shader.FindKernel("ScorePairs");
                int wordsPerView = batch.Words / batch.Views;
                shader.SetInt("_ViewCount", batch.Views); shader.SetInt("_ShapeCount", batch.Shapes);
                shader.SetInt("_WordsPerView", wordsPerView); shader.SetInt("_Resolution", Context.Resolution);
                shader.SetBuffer(render, "_PolygonPoints", polygons);
                shader.SetBuffer(render, "_Projections", projections); shader.SetBuffer(render, "_Masks", masks);
                shader.SetBuffer(score, "_Masks", masks); shader.SetBuffer(score, "_Reference", reference);
                shader.SetBuffer(score, "_Backgrounds", backgrounds); shader.SetBuffer(score, "_Pairs", pairs);
                shader.SetBuffer(score, "_Counts", counts);
                shader.Dispatch(render, (wordsPerView + 127) / 128, batch.Views, batch.Shapes);
                shader.Dispatch(score, batch.Pairs.Length, batch.Views, 1);
                var result = new uint[counts.count]; counts.GetData(result);
                DispatchBatches++;
                return result;
            }
            finally { foreach (var buffer in buffers) buffer.Dispose(); }
        }

        private static uint[] Words(IEnumerable<ulong[]> arrays)
        {
            var words = new List<uint>();
            foreach (var array in arrays)
                foreach (ulong word in array) { words.Add((uint)word); words.Add((uint)(word >> 32)); }
            return words.ToArray();
        }

        private static Projection Project(Shape shape, Context.View view, List<Vector2> polygons)
        {
            Vector2 P(Vector3 p) => new Vector2(Vector3.Dot(p, view.U), Vector3.Dot(p, view.V));
            var result = new Projection { lowStep = new Vector4(view.Low.x, view.Low.y, view.Step.x, view.Step.y) };
            Vector2 low, high;
            if (shape.Type == Kind.Box)
            {
                var corners = new List<Vector2>();
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            corners.Add(P(shape.Center + shape.Rotation * Vector3.Scale(shape.Size * .5f, new Vector3(x, y, z))));
                var polygon = Context.Hull(corners);
                if (polygon.Length < 3) { result.shape.x = 2; return result; }
                float area = 0; low = high = polygon[0];
                for (int i = 0; i < polygon.Length; i++)
                {
                    var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
                    area += a.x * b.y - a.y * b.x;
                    low = Vector2.Min(low, a); high = Vector2.Max(high, a);
                }
                if (Mathf.Abs(area) < 1e-12f) { result.shape.x = 2; return result; }
                result.shape = new Vector4(0, polygon.Length, area > 0 ? 1 : -1, polygons.Count);
                polygons.AddRange(polygon);
            }
            else
            {
                Vector3 axis = shape.Rotation * Vector3.up;
                float half = shape.Type == Kind.Sphere ? 0 : Mathf.Max(0, shape.Height * .5f - shape.Radius);
                Vector2 a = P(shape.Center - axis * half), b = P(shape.Center + axis * half);
                Vector2 segment = b - a;
                result.shape = new Vector4(1, 0, 0, shape.Radius * shape.Radius);
                result.segment = new Vector4(a.x, a.y, b.x, b.y);
                result.parameters = new Vector4(segment.x, segment.y, segment.sqrMagnitude, 0);
                low = Vector2.Min(a, b) - Vector2.one * shape.Radius;
                high = Vector2.Max(a, b) + Vector2.one * shape.Radius;
            }
            int Pixel(float value, float origin, float step) => Mathf.Clamp((int)Math.Floor((value - origin) / step), 0, Context.Resolution - 1);
            result.bounds = new Vector4(Pixel(low.x, view.Low.x, view.Step.x), Pixel(low.y, view.Low.y, view.Step.y),
                Pixel(high.x, view.Low.x, view.Step.x), Pixel(high.y, view.Low.y, view.Step.y));
            return result;
        }

        public void Dispose()
        {
            reference?.Dispose();
            if (shader != null) UnityEngine.Object.DestroyImmediate(shader);
        }
    }
}
