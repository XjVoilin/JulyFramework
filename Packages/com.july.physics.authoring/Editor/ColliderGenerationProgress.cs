using System;
using UnityEngine;

namespace July.Physics.Authoring
{
    public readonly struct ColliderGenerationProgress
    {
        public float Fraction { get; }
        public string Stage { get; }
        internal ColliderGenerationProgress(float fraction, string stage) { Fraction = fraction; Stage = stage; }
    }

    // Each stage owns a range of the work bar and reports completed work within it.
    // This describes algorithm work, never an estimate of remaining wall-clock time.
    internal sealed class ColliderFitProgress
    {
        readonly Action<float, string> write;
        readonly float start, length;

        internal ColliderFitProgress(IProgress<ColliderGenerationProgress> sink)
        {
            length = 1;
            float last = 0;
            write = (fraction, stage) =>
            {
                last = Mathf.Max(last, fraction);
                sink?.Report(new ColliderGenerationProgress(last, stage));
            };
        }

        ColliderFitProgress(Action<float, string> write, float start, float length)
        { this.write = write; this.start = start; this.length = length; }

        internal ColliderFitProgress Slice(float from, float to) =>
            new ColliderFitProgress(write, start + length * from, length * (to - from));
        internal void Report(float fraction, string stage) => write(start + length * Mathf.Clamp01(fraction), stage);
    }
}
