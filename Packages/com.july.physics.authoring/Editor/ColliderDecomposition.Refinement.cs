using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Shape = July.Physics.Authoring.PrimitiveColliderFitter.Shape;

namespace July.Physics.Authoring
{
    internal static partial class ColliderDecomposition
    {
        private sealed class ContourJob
        {
            internal Shape Shape;
            internal ulong[] Background;
            internal VolumeProbe[] Others;
            internal float Error, Allowed, Score;
        }

        private static ContourJob Contour(Shape shape, ulong[] background, Shape[] others,
            Silhouettes silhouette, CandidateCache cache, float budget)
        {
            var job = new ContourJob { Shape = shape, Background = background,
                Others = others.Select(cache.Probe).ToArray() };
            job.Error = silhouette.Error(background, cache.Mask(shape));
            job.Allowed = Mathf.Max(budget, job.Error);
            job.Score = job.Error + (job.Others.Length == 0 ? 0 : OverlapPenalty(cache.Probe(shape), job.Others));
            return job;
        }

        // Jobs in a batch share no mutable shape/background state. Their candidates can
        // be rasterized together; each job still compares in its original candidate order.
        private static void SelectContours(ContourJob[] jobs, Shape[][] candidates, Silhouettes silhouette,
            CandidateCache cache, ColliderGpuScoring scoring, CancellationToken token)
        {
            var errors = scoring?.ScoreCandidates(silhouette, candidates, jobs.Select(j => j.Background).ToArray());
            for (int i = 0; i < jobs.Length; i++)
            {
                var job = jobs[i];
                for (int c = 0; c < candidates[i].Length; c++)
                {
                    token.ThrowIfCancellationRequested();
                    var candidate = candidates[i][c];
                    float error = errors == null ? silhouette.Error(job.Background, cache.Mask(candidate)) : errors[i][c];
                    if (error > job.Allowed + 1e-6f) continue;
                    float score = error + (job.Others.Length == 0 ? 0 : OverlapPenalty(cache.Probe(candidate), job.Others));
                    if (score < job.Score - 1e-6f)
                    {
                        job.Shape = candidate; job.Error = error; job.Score = score;
                    }
                }
            }
        }

        private static void RefineContours(ContourJob[] jobs, Silhouettes silhouette, CandidateCache cache,
            float budget, float minimum, ColliderGpuScoring scoring, CancellationToken token, ColliderFitProgress progress = null)
        {
            foreach (var job in jobs)
            {
                job.Error = silhouette.Error(job.Background, cache.Mask(job.Shape));
                job.Allowed = Mathf.Max(budget, job.Error);
                job.Score = job.Error + (job.Others.Length == 0 ? 0 : OverlapPenalty(cache.Probe(job.Shape), job.Others));
            }
            var steps = new[] { .12f, .06f, .03f };
            for (int s = 0; s < steps.Length; s++)
            {
                var active = jobs;
                for (int iteration = 0; iteration < 3 && active.Length > 0; iteration++)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report((s * 3 + iteration) / 9f,
                        $"轮廓容差简化：微调 {s + 1}/3 档，{iteration + 1}/3 轮，{active.Length} 组");
                    var previous = active.Select(j => j.Shape).ToArray();
                    var candidates = active.Select(j => Adjustments(j.Shape, steps[s], minimum).ToArray()).ToArray();
                    SelectContours(active, candidates, silhouette, cache, scoring, token);
                    var changed = new List<ContourJob>();
                    for (int i = 0; i < active.Length; i++)
                        if (!ReferenceEquals(previous[i], active[i].Shape)) changed.Add(active[i]);
                    active = changed.ToArray();
                }
            }
            progress?.Report(1, "轮廓容差简化：本轮候选微调完成");
        }
    }
}
