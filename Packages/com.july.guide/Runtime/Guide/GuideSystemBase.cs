using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using UnityEngine;

namespace July.Guide
{
    public abstract class GuideSystemBase : SystemBase, IGuideSystem
    {
        private enum RequestedExit
        {
            None,
            Skipped,
            Expired,
            Shutdown
        }

        private readonly Dictionary<int, IGuideConditionHandler> _conditions = new();
        private readonly Dictionary<int, IGuideActionHandler> _actions = new();
        private readonly Dictionary<int, IGuideCompletionHandler> _completions = new();
        private readonly Dictionary<int, IGuideViewHandler> _views = new();
        private readonly Dictionary<int, IGuideTarget> _targets = new();

        private GuideStore _store;
        private Camera _worldCamera;
        private CancellationTokenSource _stepCancellation;
        private UniTaskCompletionSource _guideFinished;
        private UniTaskCompletionSource _targetChanged = new();
        private RequestedExit _requestedExit;
        private bool _isEvaluating;
        private bool _reevaluate;
        private bool _shuttingDown;

        public bool IsRunning { get; private set; }
        public int CurrentGuideId => _store.Progress.CurrentGuideId;
        public int CurrentStepId => _store.Progress.CurrentStepId;
        internal Camera WorldCamera => _worldCamera;

        protected sealed override UniTask OnInitializeAsync()
        {
            _shuttingDown = false;
            _store = GetStore<GuideStore>();
            RegisterBuiltIns();
            OnConfigure();
            NotifyGuideStateChanged();
            return UniTask.CompletedTask;
        }

        protected sealed override void OnShutdown()
        {
            _shuttingDown = true;
            _requestedExit = RequestedExit.Shutdown;
            _stepCancellation?.Cancel();
            OnDispose();
            foreach (var view in _views.Values)
                view.Dispose();
            _conditions.Clear();
            _actions.Clear();
            _completions.Clear();
            _views.Clear();
            _targets.Clear();
            _worldCamera = null;
        }

        protected abstract void OnConfigure();

        protected virtual void OnDispose()
        {
        }

        protected void RegisterConditionHandler(IGuideConditionHandler handler)
            => RegisterUnique(_conditions, handler.Type, handler, nameof(IGuideConditionHandler));

        protected void RegisterActionHandler(IGuideActionHandler handler)
            => RegisterUnique(_actions, handler.Type, handler, nameof(IGuideActionHandler));

        protected void RegisterCompletionHandler(IGuideCompletionHandler handler)
            => RegisterUnique(_completions, handler.Type, handler, nameof(IGuideCompletionHandler));

        protected void RegisterViewHandler(IGuideViewHandler handler)
            => RegisterUnique(_views, handler.Type, handler, nameof(IGuideViewHandler));

        protected void NotifyGuideStateChanged()
        {
            if (_shuttingDown)
                return;

            if (IsRunning)
            {
                if (HasExpired(_store.GetGuide(CurrentGuideId)) ||
                    CurrentStepId != 0 && !_store.IsStepCompleted(CurrentStepId) &&
                    HasExpired(_store.GetStep(CurrentStepId)))
                    RequestExit(RequestedExit.Expired);
                return;
            }

            if (_isEvaluating)
            {
                _reevaluate = true;
                return;
            }

            EvaluateAndStartAsync().Forget(Debug.LogException);
        }

        public void RegisterTarget(IGuideTarget target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (_targets.TryGetValue(target.TargetId, out var registered) && registered != target)
                throw new InvalidOperationException($"Guide target id {target.TargetId} is already registered.");

            _targets[target.TargetId] = target;
            SignalTargetChanged();
        }

        public void UnregisterTarget(IGuideTarget target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (_targets.TryGetValue(target.TargetId, out var registered) && registered == target)
            {
                _targets.Remove(target.TargetId);
                SignalTargetChanged();
            }
        }

        public void RegisterWorldCamera(Camera camera)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));
            if (_worldCamera != null && _worldCamera != camera)
                throw new InvalidOperationException("A world guide camera is already registered.");
            _worldCamera = camera;
            SignalTargetChanged();
        }

        public void UnregisterWorldCamera(Camera camera)
        {
            if (_worldCamera == camera)
            {
                _worldCamera = null;
                SignalTargetChanged();
            }
        }

        public async UniTask<bool> SkipCurrentGuideAsync()
        {
            if (!IsRunning || !_store.GetGuide(CurrentGuideId).CanSkip)
                return false;

            var finished = _guideFinished.Task;
            RequestExit(RequestedExit.Skipped);
            await finished;
            return true;
        }

        private void RegisterBuiltIns()
        {
            RegisterViewHandler(new EmptyGuideViewHandler());
            RegisterViewHandler(new DefaultGuideViewHandler());
        }

        private async UniTask EvaluateAndStartAsync()
        {
            _isEvaluating = true;
            try
            {
                do
                {
                    _reevaluate = false;
                    var guide = SelectGuide();
                    if (guide != null)
                        await RunGuideAsync(guide);
                } while (_reevaluate && !IsRunning && !_shuttingDown);
            }
            finally
            {
                _isEvaluating = false;
            }
        }

        private GuideDefinition SelectGuide()
        {
            if (_store.Guides.Count == 0)
                return null;

            if (_store.Progress.CurrentGuideId != 0)
            {
                var current = _store.GetGuide(_store.Progress.CurrentGuideId);
                if (!HasExpired(current))
                    return current;
                FinishGuide(current.Id, GuideExitReasons.Expired);
            }

            GuideDefinition selected = null;
            foreach (var candidate in _store.Guides)
            {
                if (_store.IsGuideCompleted(candidate.Id) || HasExpired(candidate) ||
                    !EvaluateAll(candidate.StartConditions))
                    continue;
                if (selected == null || candidate.Priority > selected.Priority ||
                    candidate.Priority == selected.Priority && candidate.Id < selected.Id)
                    selected = candidate;
            }

            return selected;
        }

        private async UniTask RunGuideAsync(GuideDefinition guide)
        {
            var stepId = ResolveStartStep(guide);
            if (stepId == 0)
            {
                FinishGuide(guide.Id, GuideExitReasons.Completed);
                _reevaluate = true;
                return;
            }

            IsRunning = true;
            _requestedExit = RequestedExit.None;
            _guideFinished = new UniTaskCompletionSource();
            _store.SetCurrent(guide.Id, stepId);
            Publish(new GuideStartedEvent(guide.Id));

            try
            {
                while (stepId != 0)
                {
                    var step = _store.GetStep(stepId);
                    if (HasExpired(guide) || HasExpired(step))
                    {
                        _requestedExit = RequestedExit.Expired;
                        break;
                    }

                    await RunStepAsync(guide, step);
                    stepId = step.NextStepId;
                    if (_requestedExit != RequestedExit.None)
                        break;
                }
            }
            catch (OperationCanceledException) when (_stepCancellation != null &&
                                                     _stepCancellation.IsCancellationRequested)
            {
            }

            var requestedExit = _requestedExit;
            if (requestedExit != RequestedExit.Shutdown)
            {
                var reason = requestedExit == RequestedExit.Skipped
                    ? GuideExitReasons.Skipped
                    : requestedExit == RequestedExit.Expired
                        ? GuideExitReasons.Expired
                        : GuideExitReasons.Completed;
                FinishGuide(guide.Id, reason);
            }

            _stepCancellation?.Dispose();
            _stepCancellation = null;
            IsRunning = false;
            _guideFinished.TrySetResult();
            _reevaluate = requestedExit != RequestedExit.Shutdown;
        }

        private async UniTask RunStepAsync(GuideDefinition guide, GuideStepDefinition step)
        {
            _stepCancellation?.Dispose();
            _stepCancellation = new CancellationTokenSource();
            var cancellationToken = _stepCancellation.Token;
            _store.SetCurrent(guide.Id, step.Id);
            Publish(new GuideStepEnteredEvent(guide.Id, step.Id));

            foreach (var actionRef in step.Actions)
                await GetHandler(_actions, actionRef.Type, "action").ExecuteAsync(actionRef.ParamId, cancellationToken);

            var target = await WaitForTargetAsync(step.TargetId, cancellationToken);
            var view = GetHandler(_views, step.View.Type, "view");
            var context = new GuideViewContext(guide.Id, step.Id, guide.CanSkip, step.InputMode,
                step.TargetId, target == null ? default : target.ScreenRect, SkipCurrentGuideAsync);

            await view.OpenAsync(context, step.View, cancellationToken);
            try
            {
                if (step.Completion.Type != GuideBuiltInTypes.ImmediateCompletion)
                {
                    var completion = GetHandler(_completions, step.Completion.Type, "completion");
                    await completion.WaitAsync(new GuideCompletionContext(guide.Id, step.Id, step.TargetId),
                        step.Completion.ParamId, cancellationToken);
                }
            }
            finally
            {
                await view.CloseAsync(CancellationToken.None);
            }

            _store.MarkStepCompleted(step.Id);
            if (step.NextStepId != 0)
                _store.SetCurrent(guide.Id, step.NextStepId);
            Publish(new GuideStepExitedEvent(guide.Id, step.Id, GuideExitReasons.Completed));
        }

        private int ResolveStartStep(GuideDefinition guide)
        {
            var stepId = _store.Progress.CurrentGuideId == guide.Id && _store.Progress.CurrentStepId != 0
                ? _store.Progress.CurrentStepId
                : guide.EntryStepId;
            while (stepId != 0 && _store.IsStepCompleted(stepId))
                stepId = _store.GetStep(stepId).NextStepId;
            return stepId;
        }

        private async UniTask<IGuideTarget> WaitForTargetAsync(int targetId, CancellationToken cancellationToken)
        {
            if (targetId == 0)
                return null;

            while (true)
            {
                if (_targets.TryGetValue(targetId, out var target) &&
                    (!(target is GuideWorldTarget) || _worldCamera != null))
                    return target;
                var changed = _targetChanged.Task;
                await changed.AttachExternalCancellation(cancellationToken);
            }
        }

        private void RequestExit(RequestedExit requestedExit)
        {
            if (_requestedExit == RequestedExit.None)
                _requestedExit = requestedExit;
            _stepCancellation?.Cancel();
        }

        private void FinishGuide(int guideId, int reason)
        {
            var stepId = _store.Progress.CurrentStepId;
            if (stepId != 0 && reason != GuideExitReasons.Completed)
                Publish(new GuideStepExitedEvent(guideId, stepId, reason));
            _store.MarkGuideCompleted(guideId);
            Publish(new GuideExitedEvent(guideId, reason));
        }

        private bool HasExpired(GuideDefinition guide) => EvaluateAny(guide.ExpireConditions);
        private bool HasExpired(GuideStepDefinition step) => EvaluateAny(step.ExpireConditions);

        private bool EvaluateAll(GuideHandlerRef[] refs)
        {
            foreach (var handlerRef in refs)
                if (!GetHandler(_conditions, handlerRef.Type, "condition").Evaluate(handlerRef.ParamId))
                    return false;
            return true;
        }

        private bool EvaluateAny(GuideHandlerRef[] refs)
        {
            foreach (var handlerRef in refs)
                if (GetHandler(_conditions, handlerRef.Type, "condition").Evaluate(handlerRef.ParamId))
                    return true;
            return false;
        }

        private void SignalTargetChanged()
        {
            _targetChanged.TrySetResult();
            _targetChanged = new UniTaskCompletionSource();
        }

        private static THandler GetHandler<THandler>(Dictionary<int, THandler> handlers, int type, string kind)
        {
            if (!handlers.TryGetValue(type, out var handler))
                throw new InvalidOperationException($"Guide {kind} handler type {type} is not registered.");
            return handler;
        }

        private static void RegisterUnique<THandler>(Dictionary<int, THandler> handlers, int type, THandler handler,
            string kind)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (type < 0)
                throw new ArgumentOutOfRangeException(nameof(type));
            if (!handlers.TryAdd(type, handler))
                throw new InvalidOperationException($"{kind} type {type} is already registered.");
        }
    }
}
