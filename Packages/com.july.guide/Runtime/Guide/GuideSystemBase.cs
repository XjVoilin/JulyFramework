using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using UnityEngine;

namespace July.Guide
{
    /// <summary>
    /// Runs one eligible guide to completion. Call after project readiness, and await StopAsync before teardown.
    /// Definitions, transient execution and durable outcomes have separate owners.
    /// All calls and owner-token cancellation run on the Unity main thread.
    /// </summary>
    public abstract class GuideSystemBase : SystemBase, IGuideSystem
    {
        private readonly Dictionary<int, GuideDefinition> _guides = new();
        private readonly Dictionary<int, IGuideTarget> _targets = new();
        private GuideStore _store;
        private GuideDefinition _current;
        private CancellationTokenSource _runCancellation;
        private UniTaskCompletionSource<GuideExitReason?> _finished;
        private UniTaskCompletionSource _targetChanged = new();
        private UniTaskCompletionSource _cancellationDispatched;
        private GuideExitReason? _requestedExit;
        private Exception _reportedFailure;
        private int _runId;
        private bool _shuttingDown;
        private bool _finishing;
        private int _stopRequests;

        public bool IsRunning => _current != null;
        public int CurrentGuideId => _current?.Id ?? 0;
        public int CurrentStepId { get; private set; }
        public string WaitingFor { get; private set; }
        public Exception LastFailure { get; private set; }
        public Camera WorldCamera { get; private set; }

        protected sealed override UniTask OnInitializeAsync()
        {
            _store = GetStore<GuideStore>();
            foreach (var guide in CreateGuides())
            {
                if (guide == null || !_guides.TryAdd(guide.Id, guide))
                    throw new InvalidOperationException("Guide definitions must be non-null and have unique ids.");
            }
            // ArchContext and scene/UI targets are not ready during System initialization.
            return UniTask.CompletedTask;
        }

        protected abstract IEnumerable<GuideDefinition> CreateGuides();
        protected abstract bool CanStart(GuideDefinition guide);
        protected virtual string ResolveText(string key) => key;

        protected abstract ProcedureBase CreateStepProcedure(GuideStepContext context);

        /// <summary>
        /// Evaluate once and run the highest-priority eligible guide. Null means no eligible content.
        /// Concurrent requests join the same execution; they never create a second step cursor.
        /// A new trigger requires an explicit call after the previous execution ends.
        /// </summary>
        public UniTask<GuideExitReason?> RunAsync(CancellationToken ct = default)
        {
            if (_shuttingDown || _stopRequests != 0)
                throw new InvalidOperationException("Cannot start a guide while stopping or shutting down.");
            ct.ThrowIfCancellationRequested();
            if (IsRunning) return _finished.Task.AttachExternalCancellation(ct);

            GuideDefinition selected = null;
            foreach (var guide in _guides.Values)
            {
                if (_store.IsFinished(guide.Id) || !CanStart(guide)) continue;
                if (selected == null || guide.Priority > selected.Priority ||
                    guide.Priority == selected.Priority && guide.Id < selected.Id)
                    selected = guide;
            }
            if (selected == null) return UniTask.FromResult<GuideExitReason?>(null);

            _current = selected;
            _runId++;
            _requestedExit = null;
            _reportedFailure = null;
            LastFailure = null;
            _runCancellation = new CancellationTokenSource();
            var finished = new UniTaskCompletionSource<GuideExitReason?>();
            _finished = finished;
            // All cancellation and completion state exists before any callback can re-enter this module.
            // Route external cancellation through the same dispatch boundary as Stop/Skip.
            // A linked CTS would bypass our handling of throwing cancellation callbacks.
            var ownerCancellation = ct.Register(() => RequestExit(GuideExitReason.Aborted));
            ExecuteRunAsync(selected, _runId, _runCancellation, ownerCancellation, finished).Forget(Debug.LogException);
            return finished.Task;
        }

        /// <summary>Prevent re-entry, cancel the active guide, and wait for its owned cleanup. Does not mark completion.</summary>
        public async UniTask StopAsync()
        {
            if (!IsRunning) return;
            var finished = _finished.Task;
            _stopRequests++;
            try
            {
                RequestExit(GuideExitReason.Aborted);
                await finished;
            }
            finally { _stopRequests--; }
        }

        public async UniTask<bool> SkipCurrentGuideAsync()
        {
            if (!IsRunning || _finishing || !_current.CanSkip) return false;
            var finished = _finished.Task;
            RequestExit(GuideExitReason.Skipped);
            return await finished == GuideExitReason.Skipped;
        }

        private async UniTask ExecuteRunAsync(GuideDefinition guide, int runId,
            CancellationTokenSource cancellation, CancellationTokenRegistration ownerCancellation,
            UniTaskCompletionSource<GuideExitReason?> finished)
        {
            Exception failure = null;
            var reason = GuideExitReason.Completed;
            var activeStep = 0;
            try
            {
                Publish(new GuideStartedEvent(guide.Id));
                foreach (var step in guide.Steps)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    CurrentStepId = activeStep = step.Id;
                    WaitingFor = null;
                    var context = new GuideStepContext(this, runId, guide, step, cancellation.Token);
                    Publish(new GuideStepEnteredEvent(guide.Id, step.Id));
                    cancellation.Token.ThrowIfCancellationRequested();
                    await RunProcedure(CreateStepProcedure(context), cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    CurrentStepId = activeStep = 0;
                    WaitingFor = null;
                    Publish(new GuideStepExitedEvent(guide.Id, step.Id, GuideExitReason.Completed));
                }
                cancellation.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                reason = _requestedExit ?? GuideExitReason.Aborted;
            }
            catch (Exception error)
            {
                failure = error;
                reason = GuideExitReason.Faulted;
            }

            // Cancel can synchronously resume this method before it finishes dispatching callbacks.
            // Let that dispatch report any exception before committing or notifying a terminal result.
            if (_cancellationDispatched != null) await _cancellationDispatched.Task;
            failure = CombineFailures(_reportedFailure, failure);
            if (failure != null) reason = GuideExitReason.Faulted;
            _finishing = true;
            CurrentStepId = 0;
            WaitingFor = null;

            try
            {
                if (reason == GuideExitReason.Completed || reason == GuideExitReason.Skipped)
                    _store.Commit(guide.Id, reason);
            }
            catch (Exception error)
            {
                // The durable outcome is already committed in memory. A dirty-notification failure
                // must stay observable, but cannot undo teaching or report it as an aborted unit.
                failure = CombineFailures(failure, error);
                if (!_store.IsFinished(guide.Id)) reason = GuideExitReason.Faulted;
            }
            finally
            {
                try
                {
                    ownerCancellation.Dispose();
                    cancellation.Dispose();
                    // Keep this run joinable until the complete terminal notification batch is over.
                    // Subscribers cannot interleave the next guide with this one's notifications.
                    SignalTargetChanged();
                    LastFailure = failure;
                    if (!_shuttingDown)
                    {
                        if (activeStep != 0)
                            PublishTerminal(new GuideStepExitedEvent(guide.Id, activeStep, reason), ref failure);
                        LastFailure = failure;
                        PublishTerminal(new GuideExitedEvent(guide.Id, reason, failure), ref failure);
                    }
                }
                catch (Exception error)
                {
                    failure = CombineFailures(failure, error);
                }
                finally
                {
                    _current = null;
                    _finished = null;
                    _runCancellation = null;
                    _reportedFailure = null;
                    _finishing = false;
                    LastFailure = failure;
                    // Completing can synchronously start a new run. Do not touch run state after this point.
                    if (failure == null) finished.TrySetResult(reason);
                    else finished.TrySetException(failure);
                }
            }
        }

        private void RequestExit(GuideExitReason reason)
        {
            if (_finishing) return;
            _requestedExit ??= reason;
            if (_runCancellation.IsCancellationRequested) return;
            var dispatched = new UniTaskCompletionSource();
            _cancellationDispatched = dispatched;
            try
            {
                _runCancellation.Cancel();
            }
            catch (Exception error)
            {
                // Cancellation callbacks are supplied by Procedures/third-party operations.
                // Surface their failure through the run, after awaiting all owned cleanup.
                _reportedFailure = CombineFailures(_reportedFailure, error);
            }
            finally
            {
                _cancellationDispatched = null;
                dispatched.TrySetResult();
            }
        }

        private void PublishTerminal<T>(T message, ref Exception failure)
        {
            try { Publish(message); }
            catch (Exception error)
            {
                // An event error handler must not leave Run/Stop waiters permanently suspended.
                failure = CombineFailures(failure, error);
            }
        }

        private static Exception CombineFailures(Exception first, Exception second)
            => first == null ? second : second == null || ReferenceEquals(first, second)
                ? first : new AggregateException(first, second);

        internal void RequestSkip(GuideStepContext context)
        {
            if (!IsCurrent(context) || !context.CanSkip) return;
            RequestExit(GuideExitReason.Skipped);
        }

        internal void Fail(GuideStepContext context, Exception error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            if (!IsCurrent(context)) return; // A closed presentation must not affect a later run.
            _reportedFailure = CombineFailures(_reportedFailure, error);
            RequestExit(GuideExitReason.Faulted);
        }

        internal void SetWaitingFor(GuideStepContext context, string description)
        {
            context.RunToken.ThrowIfCancellationRequested();
            if (!IsCurrent(context)) throw new InvalidOperationException("This guide execution has ended.");
            WaitingFor = description;
        }

        private bool IsCurrent(GuideStepContext context) =>
            IsRunning && !_finishing && context.RunId == _runId && context.Step.Id == CurrentStepId;

        internal string GetText(string key) => ResolveText(key);

        public void RegisterTarget(IGuideTarget target)
        {
            if (target == null || target.TargetId <= 0)
                throw new ArgumentException("Guide targets require a positive id.", nameof(target));
            if (_targets.TryGetValue(target.TargetId, out var previous) && previous != target)
                throw new InvalidOperationException($"Guide target {target.TargetId} is already registered.");
            _targets[target.TargetId] = target;
            SignalTargetChanged();
        }

        public void UnregisterTarget(IGuideTarget target)
        {
            if (_targets.TryGetValue(target.TargetId, out var previous) && previous == target)
            {
                _targets.Remove(target.TargetId);
                SignalTargetChanged();
            }
        }

        public void RegisterWorldCamera(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (WorldCamera != null && WorldCamera != camera)
                throw new InvalidOperationException("A guide world camera is already registered.");
            WorldCamera = camera;
            SignalTargetChanged();
        }

        public void UnregisterWorldCamera(Camera camera)
        {
            if (WorldCamera != camera) return;
            WorldCamera = null;
            SignalTargetChanged();
        }

        internal bool IsTargetRegistered(IGuideTarget target) =>
            target != null && _targets.TryGetValue(target.TargetId, out var current) && current == target &&
            (!(target is GuideWorldTarget) || WorldCamera != null);

        internal async UniTask<IGuideTarget> WaitForTargetAsync(GuideStepContext context, int targetId, CancellationToken ct)
        {
            if (targetId < 0) throw new ArgumentOutOfRangeException(nameof(targetId));
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RunToken, ct);
            var token = cancellation.Token;
            token.ThrowIfCancellationRequested();
            if (targetId == 0) return null;
            context.SetWaitingFor($"Target {targetId}");
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (_targets.TryGetValue(targetId, out var target) && IsTargetRegistered(target)) return target;
                await _targetChanged.Task.AttachExternalCancellation(token);
            }
        }

        private void SignalTargetChanged()
        {
            var previous = _targetChanged;
            _targetChanged = new UniTaskCompletionSource();
            previous.TrySetResult();
        }

        protected sealed override void OnShutdown()
        {
            _shuttingDown = true;
            if (IsRunning)
            {
                // Normal scene/application teardown must await StopAsync first.
                // Do not destroy a view still owned by an unwinding Procedure.
                RequestExit(GuideExitReason.Aborted);
            }
            _targets.Clear();
            WorldCamera = null;
        }
    }
}
