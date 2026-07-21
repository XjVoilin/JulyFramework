using System;
using System.Collections.Generic;

namespace July.Tasks
{
    /// <summary>
    /// 不可变的任务阶段数据。
    /// 阶段在任务中的下标就是它的定位信息，不额外保存阶段标识。
    /// </summary>
    public readonly struct TaskStageData
    {
        /// <summary>大于 0 的累计目标值。</summary>
        public long TargetValue { get; }

        /// <summary>阶段的核心状态。</summary>
        public TaskState State { get; }

        public TaskStageData(long targetValue, TaskState state)
        {
            TargetValue = targetValue;
            State = state;
        }
    }

    /// <summary>
    /// 不可变的完整任务数据，同时用于命令输入和查询快照。
    /// 具体序列化方案使用的 DTO 应由调用方与此类型进行显式转换。
    /// </summary>
    public readonly struct TaskData
    {
        /// <summary>全局唯一的正整数任务标识。</summary>
        public int TaskId { get; }

        /// <summary>所有阶段共享的非负累计事实值。</summary>
        public long CurrentValue { get; }

        /// <summary>按业务顺序排列的阶段快照，至少包含一个阶段。</summary>
        public IReadOnlyList<TaskStageData> Stages { get; }

        public TaskData(int taskId, long currentValue, IReadOnlyList<TaskStageData> stages)
        {
            TaskId = taskId;
            CurrentValue = currentValue;

            if (stages == null)
            {
                Stages = null;
                return;
            }

            var snapshot = new TaskStageData[stages.Count];
            for (var index = 0; index < stages.Count; index++)
                snapshot[index] = stages[index];

            Stages = Array.AsReadOnly(snapshot);
        }
    }
}
