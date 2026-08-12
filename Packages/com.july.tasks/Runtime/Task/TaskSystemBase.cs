using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;

namespace July.Tasks
{
    /// <summary>
    /// 任务核心接入 July 系统生命周期的实现。
    /// 项目只需注册一个派生系统，并可在同一实例上增加项目能力，但不能重写核心任务命令。
    /// </summary>
    public abstract class TaskSystemBase : SystemBase, ITaskSystem
    {
        private readonly Queue<PendingTaskEvent> _eventQueue = new();
        private Dictionary<int, TaskData> _tasks = new();
        private bool _isDispatching;

        protected sealed override UniTask OnInitializeAsync()
        {
            _tasks = new Dictionary<int, TaskData>();
            _eventQueue.Clear();
            _isDispatching = false;
            OnConfigure();
            return UniTask.CompletedTask;
        }

        protected sealed override void OnShutdown()
        {
            try
            {
                OnDispose();
            }
            finally
            {
                _tasks.Clear();
                _eventQueue.Clear();
                _isDispatching = false;
            }
        }

        /// <summary>任务核心准备完成后调用的可选项目配置钩子。</summary>
        protected virtual void OnConfigure()
        {
        }

        /// <summary>任务核心清理之前调用的可选项目释放钩子。</summary>
        protected virtual void OnDispose()
        {
        }

        public bool RegisterTask(TaskData task)
        {
            if (!IsValidTask(task))
                return false;

            return _tasks.TryAdd(task.TaskId, task);
        }

        public bool RemoveTask(int taskId)
        {
            return _tasks.Remove(taskId);
        }

        public bool ReplaceAllTasks(IReadOnlyList<TaskData> tasks)
        {
            if (tasks == null)
                return false;

            var replacement = new Dictionary<int, TaskData>(tasks.Count);
            for (var index = 0; index < tasks.Count; index++)
            {
                var task = tasks[index];
                if (!IsValidTask(task) || !replacement.TryAdd(task.TaskId, task))
                    return false;
            }

            _tasks = replacement;
            _eventQueue.Enqueue(PendingTaskEvent.CollectionReplaced());
            DrainEvents();
            return true;
        }

        public bool ReplaceTasks(IReadOnlyList<TaskData> tasks)
        {
            if (tasks == null)
                return false;

            var taskIds = new HashSet<int>();
            for (var index = 0; index < tasks.Count; index++)
            {
                var task = tasks[index];
                if (!IsValidTask(task) ||
                    !_tasks.ContainsKey(task.TaskId) ||
                    !taskIds.Add(task.TaskId))
                {
                    return false;
                }
            }

            for (var index = 0; index < tasks.Count; index++)
            {
                var task = tasks[index];
                _tasks[task.TaskId] = task;
                _eventQueue.Enqueue(PendingTaskEvent.TaskReplaced(task.TaskId));
            }

            DrainEvents();
            return true;
        }

        public bool SetCurrentValue(int taskId, long currentValue)
        {
            if (currentValue < 0 || !_tasks.TryGetValue(taskId, out var task))
                return false;

            if (currentValue <= task.CurrentValue || !HasActiveStage(task))
                return true;

            var stages = CopyStages(task);
            for (var stageIndex = 0; stageIndex < stages.Length; stageIndex++)
            {
                var stage = stages[stageIndex];
                if (stage.State == TaskState.Active && currentValue >= stage.TargetValue)
                {
                    stages[stageIndex] = new TaskStageData(
                        stage.TargetValue,
                        TaskState.Completed);
                }
            }

            _tasks[taskId] = new TaskData(taskId, currentValue, stages);
            _eventQueue.Enqueue(PendingTaskEvent.ValueChanged(
                taskId,
                task.CurrentValue,
                currentValue));

            for (var stageIndex = 0; stageIndex < stages.Length; stageIndex++)
            {
                var previousState = task.Stages[stageIndex].State;
                var currentState = stages[stageIndex].State;
                if (previousState != currentState)
                {
                    _eventQueue.Enqueue(PendingTaskEvent.StageStateChanged(
                        taskId,
                        stageIndex,
                        previousState,
                        currentState));
                }
            }

            DrainEvents();
            return true;
        }

        public bool ClaimStage(int taskId, int stageIndex)
        {
            if (!_tasks.TryGetValue(taskId, out var task) ||
                stageIndex < 0 ||
                stageIndex >= task.Stages.Count)
            {
                return false;
            }

            var stage = task.Stages[stageIndex];
            if (stage.State == TaskState.Claimed)
                return true;

            if (stage.State != TaskState.Completed)
                return false;

            var stages = CopyStages(task);
            stages[stageIndex] = new TaskStageData(stage.TargetValue, TaskState.Claimed);
            _tasks[taskId] = new TaskData(task.TaskId, task.CurrentValue, stages);
            _eventQueue.Enqueue(PendingTaskEvent.StageStateChanged(
                taskId,
                stageIndex,
                TaskState.Completed,
                TaskState.Claimed));
            DrainEvents();
            return true;
        }

        public bool ResetTask(int taskId)
        {
            if (!_tasks.TryGetValue(taskId, out var task))
                return false;

            if (IsReset(task))
                return true;

            _tasks[taskId] = CreateResetTask(task);
            EnqueueResetEvents(task);
            DrainEvents();
            return true;
        }

        public bool ResetAllTasks()
        {
            if (_tasks.Count == 0)
                return true;

            var taskIds = new int[_tasks.Count];
            _tasks.Keys.CopyTo(taskIds, 0);
            for (var index = 0; index < taskIds.Length; index++)
            {
                var taskId = taskIds[index];
                var task = _tasks[taskId];
                if (IsReset(task))
                    continue;

                _tasks[taskId] = CreateResetTask(task);
                EnqueueResetEvents(task);
            }

            DrainEvents();
            return true;
        }

        public bool TryGetTask(int taskId, out TaskData task)
        {
            return _tasks.TryGetValue(taskId, out task);
        }

        public IReadOnlyList<TaskData> GetAllTasks()
        {
            var snapshot = new TaskData[_tasks.Count];
            var index = 0;
            foreach (var task in _tasks.Values)
                snapshot[index++] = task;

            return snapshot;
        }

        private void EnqueueResetEvents(TaskData task)
        {
            if (task.CurrentValue != 0)
            {
                _eventQueue.Enqueue(PendingTaskEvent.ValueChanged(
                    task.TaskId,
                    task.CurrentValue,
                    0));
            }

            for (var stageIndex = 0; stageIndex < task.Stages.Count; stageIndex++)
            {
                var previousState = task.Stages[stageIndex].State;
                if (previousState != TaskState.Active)
                {
                    _eventQueue.Enqueue(PendingTaskEvent.StageStateChanged(
                        task.TaskId,
                        stageIndex,
                        previousState,
                        TaskState.Active));
                }
            }
        }

        private static TaskData CreateResetTask(TaskData task)
        {
            var stages = new TaskStageData[task.Stages.Count];
            for (var stageIndex = 0; stageIndex < stages.Length; stageIndex++)
            {
                stages[stageIndex] = new TaskStageData(
                    task.Stages[stageIndex].TargetValue,
                    TaskState.Active);
            }

            return new TaskData(task.TaskId, 0, stages);
        }

        private static TaskStageData[] CopyStages(TaskData task)
        {
            var stages = new TaskStageData[task.Stages.Count];
            for (var stageIndex = 0; stageIndex < stages.Length; stageIndex++)
                stages[stageIndex] = task.Stages[stageIndex];

            return stages;
        }

        private static bool HasActiveStage(TaskData task)
        {
            for (var stageIndex = 0; stageIndex < task.Stages.Count; stageIndex++)
            {
                if (task.Stages[stageIndex].State == TaskState.Active)
                    return true;
            }

            return false;
        }

        private static bool IsReset(TaskData task)
        {
            if (task.CurrentValue != 0)
                return false;

            for (var stageIndex = 0; stageIndex < task.Stages.Count; stageIndex++)
            {
                if (task.Stages[stageIndex].State != TaskState.Active)
                    return false;
            }

            return true;
        }

        private static bool IsValidTask(TaskData task)
        {
            if (task.TaskId <= 0 ||
                task.CurrentValue < 0 ||
                task.Stages == null ||
                task.Stages.Count == 0)
            {
                return false;
            }

            for (var stageIndex = 0; stageIndex < task.Stages.Count; stageIndex++)
            {
                var stage = task.Stages[stageIndex];
                if (stage.TargetValue <= 0 || !IsValidState(stage.State))
                    return false;
            }

            return true;
        }

        private static bool IsValidState(TaskState state)
        {
            return state == TaskState.Active ||
                   state == TaskState.Completed ||
                   state == TaskState.Claimed;
        }

        private void DrainEvents()
        {
            if (_isDispatching)
                return;

            _isDispatching = true;
            try
            {
                while (_eventQueue.Count > 0)
                {
                    var pendingEvent = _eventQueue.Dequeue();
                    try
                    {
                        PublishPendingEvent(pendingEvent);
                    }
                    catch (Exception exception)
                    {
                        JLogger.LogException(exception);
                    }
                }
            }
            finally
            {
                _isDispatching = false;
            }
        }

        private void PublishPendingEvent(PendingTaskEvent pendingEvent)
        {
            switch (pendingEvent.Kind)
            {
                case PendingTaskEventKind.ValueChanged:
                    Publish(new TaskValueChangedEvent(
                        pendingEvent.TaskId,
                        pendingEvent.PreviousValue,
                        pendingEvent.CurrentValue));
                    break;
                case PendingTaskEventKind.StageStateChanged:
                    Publish(new TaskStageStateChangedEvent(
                        pendingEvent.TaskId,
                        pendingEvent.StageIndex,
                        pendingEvent.PreviousState,
                        pendingEvent.CurrentState));
                    break;
                case PendingTaskEventKind.CollectionReplaced:
                    Publish(new TaskCollectionReplacedEvent());
                    break;
                case PendingTaskEventKind.TaskReplaced:
                    Publish(new TaskReplacedEvent(pendingEvent.TaskId));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(pendingEvent),
                        pendingEvent.Kind,
                        "Unknown pending task event kind.");
            }
        }
    }

    internal enum PendingTaskEventKind
    {
        ValueChanged,
        StageStateChanged,
        CollectionReplaced,
        TaskReplaced
    }

    internal readonly struct PendingTaskEvent
    {
        public PendingTaskEventKind Kind { get; }
        public int TaskId { get; }
        public int StageIndex { get; }
        public long PreviousValue { get; }
        public long CurrentValue { get; }
        public TaskState PreviousState { get; }
        public TaskState CurrentState { get; }

        private PendingTaskEvent(
            PendingTaskEventKind kind,
            int taskId,
            int stageIndex,
            long previousValue,
            long currentValue,
            TaskState previousState,
            TaskState currentState)
        {
            Kind = kind;
            TaskId = taskId;
            StageIndex = stageIndex;
            PreviousValue = previousValue;
            CurrentValue = currentValue;
            PreviousState = previousState;
            CurrentState = currentState;
        }

        public static PendingTaskEvent ValueChanged(
            int taskId,
            long previousValue,
            long currentValue)
        {
            return new PendingTaskEvent(
                PendingTaskEventKind.ValueChanged,
                taskId,
                0,
                previousValue,
                currentValue,
                default,
                default);
        }

        public static PendingTaskEvent StageStateChanged(
            int taskId,
            int stageIndex,
            TaskState previousState,
            TaskState currentState)
        {
            return new PendingTaskEvent(
                PendingTaskEventKind.StageStateChanged,
                taskId,
                stageIndex,
                0,
                0,
                previousState,
                currentState);
        }

        public static PendingTaskEvent CollectionReplaced()
        {
            return new PendingTaskEvent(
                PendingTaskEventKind.CollectionReplaced,
                0,
                0,
                0,
                0,
                default,
                default);
        }

        public static PendingTaskEvent TaskReplaced(int taskId)
        {
            return new PendingTaskEvent(
                PendingTaskEventKind.TaskReplaced,
                taskId,
                0,
                0,
                0,
                default,
                default);
        }
    }
}
