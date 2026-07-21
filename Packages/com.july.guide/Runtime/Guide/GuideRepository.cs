using System;
using System.Collections.Generic;

namespace July.Guide
{
    public sealed class GuideRepository
    {
        private readonly GuideProgressData _data;
        private readonly Action _markDirty;

        public GuideRepository(GuideProgressData data, Action markDirty)
        {
            _data = data ?? new GuideProgressData();
            _markDirty = markDirty;
        }

        public bool IsFlowCompleted(string flowId)
        {
            return !string.IsNullOrEmpty(flowId) && _data.completedFlows.Contains(flowId);
        }

        public bool IsStepCompleted(string flowId, string stepId)
        {
            if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(stepId))
                return false;

            return _data.completedSteps.Contains(GetStepKey(flowId, stepId));
        }

        public string GetCurrentFlowId() => _data.currentFlowId;
        public string GetCurrentStepId() => _data.currentStepId;

        public void SetCurrentStep(string flowId, string stepId)
        {
            _data.currentFlowId = flowId;
            _data.currentStepId = stepId;
            _markDirty?.Invoke();
        }

        public void ClearCurrentStep()
        {
            _data.currentFlowId = null;
            _data.currentStepId = null;
            _markDirty?.Invoke();
        }

        public void MarkFlowCompleted(string flowId)
        {
            if (string.IsNullOrEmpty(flowId)) return;

            _data.completedFlows.Add(flowId);
            _markDirty?.Invoke();
        }

        public void MarkStepCompleted(string flowId, string stepId)
        {
            if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(stepId)) return;

            _data.completedSteps.Add(GetStepKey(flowId, stepId));
            _markDirty?.Invoke();
        }

        public void ClearProgress()
        {
            _data.completedFlows.Clear();
            _data.completedSteps.Clear();
            _data.currentFlowId = null;
            _data.currentStepId = null;
            _markDirty?.Invoke();
        }

        private static string GetStepKey(string flowId, string stepId) => $"{flowId}:{stepId}";
    }
}
