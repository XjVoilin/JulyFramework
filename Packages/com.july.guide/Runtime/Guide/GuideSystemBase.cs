using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using UnityEngine;

namespace July.Guide
{
    /// <summary>
    /// 执行一段满足条件的引导直至结束；项目就绪后调用，清理前等待 StopAsync 完成。
    /// 引导定义、临时执行状态和持久结果分别管理。
    /// 所有调用均在 Unity 主线程执行。
    /// </summary>
    public abstract class GuideSystemBase : SystemBase, IGuideSystem
    {
        private readonly Dictionary<int, GuidePlan> _guides = new();
        private readonly Dictionary<int, IGuideStartCondition> _startConditions = new();
        private GuideStore _store;
        private GuidePlan _current;
        private CancellationTokenSource _runCancellation;
        private UniTaskCompletionSource<GuideExitReason?> _finished;
        private GuideExitReason? _requestedExit;
        private GuideStepContext _currentStepContext;
        private bool _shuttingDown;
        private bool _finishing;

        public bool IsRunning => _current != null;
        public int CurrentGuideId => _current?.Id ?? 0;
        public int CurrentStepId { get; private set; }
        public string WaitingFor { get; private set; }
        public Exception LastFailure { get; private set; }

        protected sealed override UniTask OnInitializeAsync()
        {
            _store = GetStore<GuideStore>();
            foreach (var condition in CreateStartConditions())
            {
                if (condition == null)
                    throw new InvalidOperationException("开始条件策略不能为空。");
                var type = condition.Type;
                if (!_startConditions.TryAdd(type, condition))
                    throw new InvalidOperationException($"开始条件类型 {type} 重复注册。");
            }

            foreach (var guide in CreatePlans())
            {
                if (guide == null || !_guides.TryAdd(guide.Id, guide))
                    throw new InvalidOperationException("Guide definitions must be non-null and have unique ids.");
                if (!_startConditions.ContainsKey(guide.StartConditionType))
                    throw new InvalidOperationException(
                        $"引导 {guide.Id} 引用了未注册的开始条件类型 {guide.StartConditionType}。");
            }

            // System 初始化期间，ArchContext 与场景/UI 目标尚未就绪。
            return UniTask.CompletedTask;
        }

        protected abstract IEnumerable<GuidePlan> CreatePlans();

        /// <summary>
        /// 初始化时调用一次，按 Type 注册项目支持的条件策略；每种类型只注册一个实例。
        /// 此时只绑定依赖，实际准入在 RunAsync 中按计划的参数引用读取最新业务状态。
        /// </summary>
        protected abstract IEnumerable<IGuideStartCondition> CreateStartConditions();

        protected abstract ProcedureBase CreateStepProcedure(GuideStepContext context);

        /// <summary>
        /// 评估一次候选，执行优先级最高且满足条件的引导；返回 null 表示本次未启动引导。
        /// 已有引导执行时直接忽略本次请求并返回 null，不等待当前执行，也不排队。
        /// 再次触发需要在上次执行结束后显式调用；未完成计划从第一个步骤重新执行。
        /// 项目须先准备或重建该教学所需的业务状态，框架不会回滚玩法或恢复步骤进度。
        /// </summary>
        public UniTask<GuideExitReason?> RunAsync()
        {
            if (_shuttingDown)
                throw new InvalidOperationException("Cannot start a guide while shutting down.");
            if (IsRunning)
                return UniTask.FromResult<GuideExitReason?>(null);

            GuidePlan selected = null;
            foreach (var guide in _guides.Values)
            {
                if (_store.IsFinished(guide.Id)) continue;
                var condition = _startConditions[guide.StartConditionType];
                if (!condition.CanStart(guide.StartConditionParamId)) continue;
                if (selected == null || guide.Priority > selected.Priority ||
                    guide.Priority == selected.Priority && guide.Id < selected.Id)
                    selected = guide;
            }

            if (selected == null) return UniTask.FromResult<GuideExitReason?>(null);

            _current = selected;
            _requestedExit = null;
            LastFailure = null;
            _runCancellation = new CancellationTokenSource();
            var finished = new UniTaskCompletionSource<GuideExitReason?>();
            _finished = finished;
            // 执行可能同步结束并清空字段，返回值仍使用本轮的完成任务。
            ExecuteRunAsync(selected, _runCancellation, finished).Forget(Debug.LogException);
            return finished.Task;
        }

        /// <summary>取消当前引导并等待其清理完成；不标记引导完成。</summary>
        public async UniTask StopAsync()
        {
            if (!IsRunning) return;
            var finished = _finished.Task;
            RequestExit(GuideExitReason.Aborted);
            await finished;
        }

        public async UniTask<bool> SkipCurrentGuideAsync()
        {
            if (!IsRunning || _finishing || !_current.CanSkip) return false;
            var finished = _finished.Task;
            RequestExit(GuideExitReason.Skipped);
            return await finished == GuideExitReason.Skipped;
        }

        private async UniTask ExecuteRunAsync(GuidePlan guide,
            CancellationTokenSource cancellation,
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
                    var context = new GuideStepContext(this, guide, step, cancellation.Token);
                    _currentStepContext = context;
                    Publish(new GuideStepEnteredEvent(guide.Id, step.Id));
                    cancellation.Token.ThrowIfCancellationRequested();
                    await RunProcedure(CreateStepProcedure(context), cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    _currentStepContext = null;
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

            _finishing = true;
            _currentStepContext = null;
            CurrentStepId = 0;
            WaitingFor = null;

            try
            {
                if (reason == GuideExitReason.Completed || reason == GuideExitReason.Skipped)
                    _store.Commit(guide.Id, reason);
            }
            catch (Exception error)
            {
                // 持久结果已在内存中提交。保存通知失败不能撤销已完成的教学。
                failure = error;
                if (!_store.IsFinished(guide.Id)) reason = GuideExitReason.Faulted;
            }

            try
            {
                cancellation.Dispose();
                LastFailure = failure;
                if (!_shuttingDown)
                {
                    if (activeStep != 0)
                        Publish(new GuideStepExitedEvent(guide.Id, activeStep, reason));
                    Publish(new GuideExitedEvent(guide.Id, reason, failure));
                }
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                _current = null;
                _finished = null;
                _runCancellation = null;
                _finishing = false;
                LastFailure = failure;
                // 完成任务可能同步启动下一次执行，此后不得再访问本次运行状态。
                if (failure == null) finished.TrySetResult(reason);
                else finished.TrySetException(failure);
            }
        }

        private void RequestExit(GuideExitReason reason)
        {
            // 已有退出请求生效时，不再更改退出原因。
            if (_finishing || _runCancellation.IsCancellationRequested) return;
            _requestedExit = reason;
            _runCancellation.Cancel();
        }

        internal void RequestSkip(GuideStepContext context)
        {
            if (!IsCurrent(context) || !context.CanSkip) return;
            RequestExit(GuideExitReason.Skipped);
        }

        internal void SetWaitingFor(GuideStepContext context, string description)
        {
            context.RunToken.ThrowIfCancellationRequested();
            if (!IsCurrent(context)) throw new InvalidOperationException("This guide execution has ended.");
            WaitingFor = description;
        }

        private bool IsCurrent(GuideStepContext context) =>
            ReferenceEquals(context, _currentStepContext);

        protected sealed override void OnShutdown()
        {
            _shuttingDown = true;
            if (IsRunning)
            {
                // 正常退出场景或应用前，必须先等待 StopAsync 完成。
                // 不要销毁仍被清理中的 Procedure 持有的视图。
                RequestExit(GuideExitReason.Aborted);
            }
        }
    }
}