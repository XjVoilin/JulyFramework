using System;
using System.Collections.Generic;
using July.Arch;

namespace July.Guide
{
    public sealed class GuideStore : StoreBase<GuideStoreData>
    {
        private readonly Dictionary<int, GuideDefinition> _guides = new();
        private readonly Dictionary<int, GuideStepDefinition> _steps = new();
        private readonly HashSet<int> _completedGuides = new();
        private readonly HashSet<int> _completedSteps = new();

        public IReadOnlyCollection<GuideDefinition> Guides => _guides.Values;
        public GuideProgressData Progress => Data.Progress;

        protected override void OnDataReplaced()
        {
            ValidateAndRebuild();
        }

        public GuideDefinition GetGuide(int guideId) => _guides[guideId];
        public GuideStepDefinition GetStep(int stepId) => _steps[stepId];
        public bool IsGuideCompleted(int guideId) => _completedGuides.Contains(guideId);
        public bool IsStepCompleted(int stepId) => _completedSteps.Contains(stepId);

        internal void SetCurrent(int guideId, int stepId)
        {
            Data.Progress.CurrentGuideId = guideId;
            Data.Progress.CurrentStepId = stepId;
            MarkDirty();
        }

        internal void MarkStepCompleted(int stepId)
        {
            if (_completedSteps.Add(stepId))
                Data.Progress.CompletedStepIds.Add(stepId);
            MarkDirty();
        }

        internal void MarkGuideCompleted(int guideId)
        {
            if (_completedGuides.Add(guideId))
                Data.Progress.CompletedGuideIds.Add(guideId);
            ClearCurrentWithoutMarkingDirty();
            MarkDirty();
        }

        private void ClearCurrentWithoutMarkingDirty()
        {
            Data.Progress.CurrentGuideId = 0;
            Data.Progress.CurrentStepId = 0;
        }

        private void ValidateAndRebuild()
        {
            _guides.Clear();
            _steps.Clear();
            _completedGuides.Clear();
            _completedSteps.Clear();

            foreach (var guide in Data.Guides)
            {
                if (guide == null || guide.Id <= 0 || guide.EntryStepId <= 0)
                    throw new InvalidOperationException("Every guide requires positive Id and EntryStepId.");
                if (!_guides.TryAdd(guide.Id, guide))
                    throw new InvalidOperationException($"Duplicate guide id: {guide.Id}.");
            }

            foreach (var step in Data.Steps)
            {
                if (step == null || step.Id <= 0 || step.GuideId <= 0)
                    throw new InvalidOperationException("Every guide step requires positive Id and GuideId.");
                if (!_guides.ContainsKey(step.GuideId))
                    throw new InvalidOperationException($"Step {step.Id} references missing guide {step.GuideId}.");
                if (!_steps.TryAdd(step.Id, step))
                    throw new InvalidOperationException($"Duplicate guide step id: {step.Id}.");
            }

            foreach (var guide in _guides.Values)
            {
                var entry = GetStep(guide.EntryStepId);
                if (entry.GuideId != guide.Id)
                    throw new InvalidOperationException($"Guide {guide.Id} entry step belongs to guide {entry.GuideId}.");
                ValidateLinearChain(guide);
            }

            foreach (var guideId in Data.Progress.CompletedGuideIds)
                _completedGuides.Add(guideId);
            foreach (var stepId in Data.Progress.CompletedStepIds)
                _completedSteps.Add(stepId);
        }

        private void ValidateLinearChain(GuideDefinition guide)
        {
            var visited = new HashSet<int>();
            var stepId = guide.EntryStepId;
            while (stepId != 0)
            {
                if (!visited.Add(stepId))
                    throw new InvalidOperationException($"Guide {guide.Id} contains a step cycle at {stepId}.");
                if (!_steps.TryGetValue(stepId, out var step) || step.GuideId != guide.Id)
                    throw new InvalidOperationException($"Guide {guide.Id} references invalid next step {stepId}.");
                stepId = step.NextStepId;
            }
        }
    }
}
