namespace July.Tasks
{
    /// <summary>
    /// 传统手游任务阶段共用的核心状态。
    /// 项目特有状态应在此枚举之外通过组合表达。
    /// </summary>
    public enum TaskState
    {
        Active = 0,
        Completed = 1,
        Claimed = 2
    }
}
