namespace July.Guide
{
    /// <summary>
    /// 引导步骤状态
    /// </summary>
    public enum GuideStepStatus
    {
        /// <summary>
        /// 待执行
        /// </summary>
        Pending,

        /// <summary>
        /// 执行中
        /// </summary>
        Active,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed,

        /// <summary>
        /// 已跳过
        /// </summary>
        Skipped
    }

    /// <summary>
    /// 引导流程状态
    /// </summary>
    public enum GuideFlowStatus
    {
        /// <summary>
        /// 空闲（未开始）
        /// </summary>
        Idle,

        /// <summary>
        /// 运行中
        /// </summary>
        Running,

        /// <summary>
        /// 已完成（包括正常完成和用户跳过）
        /// </summary>
        Completed
    }
}

