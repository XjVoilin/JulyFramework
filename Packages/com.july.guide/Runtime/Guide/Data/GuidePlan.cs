using System;
using System.Collections.Generic;

namespace July.Guide
{
    /// <summary>整段引导计划，包含开始条件、步骤顺序、优先级和跳过策略；具体规则由项目提供。
    /// 仅持久化整段完成或跳过结果；中断后再次执行从第一个步骤开始，不保存步骤进度。</summary>
    public sealed class GuidePlan
    {
        public int Id { get; }
        public IGuideStartCondition StartCondition { get; }
        public int Priority { get; }
        public bool CanSkip { get; }
        public IReadOnlyList<GuideStep> Steps { get; }

        public GuidePlan(int id, IReadOnlyList<GuideStep> steps,
            IGuideStartCondition startCondition, int priority = 0, bool canSkip = false)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (steps == null || steps.Count == 0)
                throw new ArgumentException("A guide must contain at least one teaching unit.", nameof(steps));
            StartCondition = startCondition ?? throw new ArgumentNullException(nameof(startCondition));
            var copy = new GuideStep[steps.Count];
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
