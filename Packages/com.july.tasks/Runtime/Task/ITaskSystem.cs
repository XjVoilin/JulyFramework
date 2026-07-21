using System.Collections.Generic;

namespace July.Tasks
{
    /// <summary>
    /// 对外提供稳定的任务核心能力。
    /// 命令返回成功时也可能没有数据变化，表示请求合法且目标结果已经满足。
    /// 普通命令失败时返回 false，不由任务模块记录日志。
    /// </summary>
    public interface ITaskSystem
    {
        /// <summary>静默注册一条完整任务数据；TaskId 重复时注册失败。</summary>
        bool RegisterTask(TaskData task);

        /// <summary>从内存任务集合中静默移除一条任务。</summary>
        bool RemoveTask(int taskId);

        /// <summary>原子替换完整任务集合，成功后发送一次集合替换标记事件。</summary>
        bool ReplaceAllTasks(IReadOnlyList<TaskData> tasks);

        /// <summary>设置绝对累计值；存在 Active 阶段时数值只能单调增加。</summary>
        bool SetCurrentValue(int taskId, long currentValue);

        /// <summary>
        /// 将指定的 Completed 阶段记录为 Claimed，不要求前置阶段已领取。
        /// 上层应先成功发放该阶段奖励，再调用此命令。
        /// </summary>
        bool ClaimStage(int taskId, int stageIndex);

        /// <summary>将一条任务重置为累计值 0、全部阶段 Active，不改变阶段目标。</summary>
        bool ResetTask(int taskId);

        /// <summary>
        /// 原子重置全部任务。只为实际变化发送既有数值和阶段状态事件，任务间事件顺序不作保证。
        /// </summary>
        bool ResetAllTasks();

        /// <summary>查询一条任务；任务不存在时不记录日志。</summary>
        bool TryGetTask(int taskId, out TaskData task);

        /// <summary>返回独立的完整任务快照，集合顺序不作保证。</summary>
        IReadOnlyList<TaskData> GetAllTasks();
    }
}
