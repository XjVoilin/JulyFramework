using System;
using System.Collections.Generic;

namespace July.Guide
{
    /// <summary>One authored tutorial. The project supplies eligibility; this definition owns order.</summary>
    public sealed class GuideDefinition
    {
        public int Id { get; }
        public int Priority { get; }
        public bool CanSkip { get; }
        public IReadOnlyList<GuideStepDefinition> Steps { get; }

        public GuideDefinition(int id, IReadOnlyList<GuideStepDefinition> steps, int priority = 0, bool canSkip = false)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (steps == null || steps.Count == 0)
                throw new ArgumentException("A guide must contain at least one teaching unit.", nameof(steps));
            var copy = new GuideStepDefinition[steps.Count];
            var ids = new HashSet<int>();
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i] ?? throw new ArgumentException("Guide steps cannot be null.", nameof(steps));
                if (!ids.Add(step.Id)) throw new ArgumentException($"Duplicate step {step.Id} in guide {id}.");
                copy[i] = step;
            }
            Id = id;
            Priority = priority;
            CanSkip = canSkip;
            Steps = Array.AsReadOnly(copy);
        }
    }
}
