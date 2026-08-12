using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using July.Arch;

namespace July.Guide
{
    public abstract class GuideSystemBase : SystemBase
    {
        private GuideRepository _repo;

        private IGuidePresenter _presenter;
        private IGuideDataProvider _dataProvider;

        private readonly Dictionary<string, IGuideFlowHandler> _flowHandlers = new();
        private IGuideFlowHandler _activeHandler;
        private bool _isFlowActive;

        protected sealed override UniTask OnInitializeAsync()
        {
            _repo = ResolveRepository();
            OnConfigure();
            return UniTask.CompletedTask;
        }

        protected abstract GuideRepository ResolveRepository();

        protected sealed override void OnShutdown()
        {
            OnDispose();

            foreach (var handler in _flowHandlers.Values)
            {
                try { handler.Dispose(); }
                catch { /* ignore */ }
            }

            _flowHandlers.Clear();
            _activeHandler = null;
            _presenter = null;
            _dataProvider = null;
        }

        protected abstract void OnConfigure();
        protected virtual void OnDispose() { }

        #region Injection

        public void SetPresenter(IGuidePresenter presenter) => _presenter = presenter;
        public void SetDataProvider(IGuideDataProvider provider) => _dataProvider = provider;

        #endregion

        #region Handler registration

        public bool RegisterFlowHandler(IGuideFlowHandler handler)
        {
            if (handler == null || string.IsNullOrEmpty(handler.FlowId))
                return false;

            if (_repo.IsFlowCompleted(handler.FlowId))
            {
                handler.Dispose();
                return false;
            }

            if (_flowHandlers.TryGetValue(handler.FlowId, out var oldHandler))
            {
                try { oldHandler.Dispose(); }
                catch { /* ignore */ }
            }

            _flowHandlers[handler.FlowId] = handler;

            try { handler.OnRegister(); }
            catch { /* ignore */ }

            return true;
        }

        public void UnregisterFlowHandler(string flowId)
        {
            if (!_flowHandlers.TryGetValue(flowId, out var handler))
                return;

            try { handler.Dispose(); }
            catch { /* ignore */ }

            _flowHandlers.Remove(flowId);
        }

        #endregion

        #region Flow control

        public bool StartFlow(string flowId)
        {
            if (string.IsNullOrEmpty(flowId))
                return false;

            if (_isFlowActive)
                return false;

            if (_repo.IsFlowCompleted(flowId))
                return false;

            var interruptedFlowId = _repo.GetCurrentFlowId();
            if (!string.IsNullOrEmpty(interruptedFlowId))
            {
                if (interruptedFlowId == flowId)
                    return ResumeFlowInternal(flowId);

                return false;
            }

            if (_dataProvider == null)
                return false;

            var flowData = _dataProvider.GetFlow(flowId);
            if (flowData == null)
                return false;

            return StartFlowInternal(flowData);
        }

        public void SkipCurrentFlow(string reason = null)
        {
            var flowId = _repo.GetCurrentFlowId();
            if (string.IsNullOrEmpty(flowId))
                return;

            var stepId = _repo.GetCurrentStepId();
            if (!string.IsNullOrEmpty(stepId) && _dataProvider != null)
            {
                var stepData = _dataProvider.GetStep(flowId, stepId);
                if (stepData != null)
                {
                    _presenter?.OnStepExit(stepData);
                    Publish(new GuideStepExitedEvent
                    {
                        FlowId = flowId,
                        StepId = stepId,
                        Completed = false
                    });
                }
            }

            DisposeHandler(flowId);
            _repo.MarkFlowCompleted(flowId);
            _repo.ClearCurrentStep();
            _isFlowActive = false;

            _presenter?.OnFlowComplete(flowId);
            Publish(new GuideFlowCompletedEvent { FlowId = flowId });
        }

        public void CompleteCurrentStep()
        {
            var flowId = _repo.GetCurrentFlowId();
            var stepId = _repo.GetCurrentStepId();

            if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(stepId))
                return;

            var stepData = _dataProvider?.GetStep(flowId, stepId);
            if (stepData == null)
                return;

            _repo.MarkStepCompleted(flowId, stepId);
            _presenter?.OnStepExit(stepData);
            Publish(new GuideStepExitedEvent
            {
                FlowId = flowId,
                StepId = stepId,
                Completed = true
            });

            if (string.IsNullOrEmpty(stepData.NextStepId))
                CompleteFlow(flowId);
            else
                AdvanceToStep(flowId, stepData.NextStepId);
        }

        public void SkipCurrentStep()
        {
            var flowId = _repo.GetCurrentFlowId();
            var stepId = _repo.GetCurrentStepId();

            if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(stepId))
                return;

            var stepData = _dataProvider?.GetStep(flowId, stepId);
            if (stepData == null)
                return;

            _presenter?.OnStepExit(stepData);
            Publish(new GuideStepExitedEvent
            {
                FlowId = flowId,
                StepId = stepId,
                Completed = false
            });

            if (string.IsNullOrEmpty(stepData.NextStepId))
                CompleteFlow(flowId);
            else
                AdvanceToStep(flowId, stepData.NextStepId);
        }

        #endregion

        #region Queries

        public bool IsFlowRunning() => _isFlowActive;
        public string GetCurrentFlowId() => _repo.GetCurrentFlowId();
        public string GetCurrentStepId() => _repo.GetCurrentStepId();
        public bool IsFlowCompleted(string flowId) => _repo.IsFlowCompleted(flowId);

        #endregion

        #region Progress

        /// <summary>清空全部引导进度（已完成流程/步骤、当前进度）。存储清理由项目层负责。</summary>
        public void ClearAll()
        {
            _repo.ClearProgress();
            _isFlowActive = false;
        }

        #endregion

        #region Private flow logic

        private bool StartFlowInternal(GuideFlowData flowData)
        {
            var flowId = flowData.FlowId;
            _isFlowActive = true;
            ActivateHandler(flowId);

            _presenter?.OnFlowStart(flowId);
            Publish(new GuideFlowStartedEvent { FlowId = flowId });

            var resumeStepId = FindResumeStepId(flowId, flowData.EntryStepId);
            if (resumeStepId == null)
            {
                CompleteFlow(flowId);
                return true;
            }

            AdvanceToStep(flowId, resumeStepId);
            return true;
        }

        private bool ResumeFlowInternal(string flowId)
        {
            var stepId = _repo.GetCurrentStepId();

            if (_dataProvider == null)
                return false;

            var flowData = _dataProvider.GetFlow(flowId);
            if (flowData == null)
            {
                _repo.ClearCurrentStep();
                return false;
            }

            string resumeStepId;
            if (!string.IsNullOrEmpty(stepId))
            {
                var stepData = _dataProvider.GetStep(flowId, stepId);
                resumeStepId = stepData != null ? stepId : FindResumeStepId(flowId, flowData.EntryStepId);
            }
            else
            {
                resumeStepId = FindResumeStepId(flowId, flowData.EntryStepId);
            }

            if (resumeStepId == null)
            {
                _isFlowActive = true;
                CompleteFlow(flowId);
                return true;
            }

            _isFlowActive = true;
            ActivateHandler(flowId);
            _presenter?.OnFlowStart(flowId);
            Publish(new GuideFlowStartedEvent { FlowId = flowId });
            AdvanceToStep(flowId, resumeStepId);
            return true;
        }

        private string FindResumeStepId(string flowId, string startStepId)
        {
            var currentStepId = startStepId;

            while (!string.IsNullOrEmpty(currentStepId))
            {
                if (!_repo.IsStepCompleted(flowId, currentStepId))
                    return currentStepId;

                var stepData = _dataProvider.GetStep(flowId, currentStepId);
                if (stepData == null) break;

                currentStepId = stepData.NextStepId;
            }

            return null;
        }

        private void AdvanceToStep(string flowId, string stepId)
        {
            if (string.IsNullOrEmpty(stepId))
            {
                CompleteFlow(flowId);
                return;
            }

            var stepData = _dataProvider?.GetStep(flowId, stepId);
            if (stepData == null)
            {
                CompleteFlow(flowId);
                return;
            }

            _repo.SetCurrentStep(flowId, stepId);
            _presenter?.OnStepEnter(stepData);
            Publish(new GuideStepEnteredEvent { FlowId = flowId, StepId = stepId });
        }

        private void CompleteFlow(string flowId)
        {
            DisposeHandler(flowId);
            _repo.MarkFlowCompleted(flowId);
            _repo.ClearCurrentStep();
            _isFlowActive = false;

            _presenter?.OnFlowComplete(flowId);
            Publish(new GuideFlowCompletedEvent { FlowId = flowId });
        }

        private void ActivateHandler(string flowId)
        {
            if (!_flowHandlers.TryGetValue(flowId, out var handler))
                return;

            _activeHandler = handler;
            try { handler.OnFlowStart(); }
            catch { /* ignore */ }
        }

        private void DisposeHandler(string flowId)
        {
            if (_activeHandler != null && _activeHandler.FlowId == flowId)
                _activeHandler = null;

            if (!_flowHandlers.TryGetValue(flowId, out var handler))
                return;

            try { handler.Dispose(); }
            catch { /* ignore */ }

            _flowHandlers.Remove(flowId);
        }

        #endregion
    }
}
