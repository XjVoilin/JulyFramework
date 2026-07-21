namespace July.Tasks
{
    /// <summary>本地命令改变任务累计值后发送。</summary>
    public readonly struct TaskValueChangedEvent
    {
        public int TaskId { get; }
        public long PreviousValue { get; }
        public long CurrentValue { get; }

        public TaskValueChangedEvent(int taskId, long previousValue, long currentValue)
        {
            TaskId = taskId;
            PreviousValue = previousValue;
            CurrentValue = currentValue;
        }
    }

    /// <summary>本地命令改变任务阶段状态后发送。</summary>
    public readonly struct TaskStageStateChangedEvent
    {
        public int TaskId { get; }
        public int StageIndex { get; }
        public TaskState PreviousState { get; }
        public TaskState CurrentState { get; }

        public TaskStageStateChangedEvent(
            int taskId,
            int stageIndex,
            TaskState previousState,
            TaskState currentState)
        {
            TaskId = taskId;
            StageIndex = stageIndex;
            PreviousState = previousState;
            CurrentState = currentState;
        }
    }

    /// <summary>
    /// 完整任务集合原子替换成功后发送的标记事件。
    /// 全量替换不会逐任务发送数值或阶段状态变化事件。
    /// </summary>
    public readonly struct TaskCollectionReplacedEvent
    {
    }
}
