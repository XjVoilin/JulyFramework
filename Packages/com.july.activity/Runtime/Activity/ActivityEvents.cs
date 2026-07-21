namespace July.Activity
{
    /// <summary>
    /// 活动开启事件
    /// 当活动首次进入"进行中"状态时触发
    /// </summary>
    public struct ActivityOpenedEvent
    {
        /// <summary>
        /// 活动 ID
        /// </summary>
        public string ActivityId;

        /// <summary>
        /// 活动定义
        /// </summary>
        public ActivityDefinition Definition;
    }

    /// <summary>
    /// 活动关闭事件
    /// 当活动进入"已结束"状态时触发
    /// </summary>
    public struct ActivityClosedEvent
    {
        /// <summary>
        /// 活动 ID
        /// </summary>
        public string ActivityId;

        /// <summary>
        /// 活动定义
        /// </summary>
        public ActivityDefinition Definition;
    }

    /// <summary>
    /// 活动进度变更事件
    /// 当活动运行时记录更新时触发
    /// </summary>
    public struct ActivityProgressChangedEvent
    {
        /// <summary>
        /// 活动 ID
        /// </summary>
        public string ActivityId;

        /// <summary>
        /// 活动运行时记录
        /// </summary>
        public ActivityRecord Record;
    }

    /// <summary>
    /// 活动模块就绪事件
    /// 当活动模块初始化完成后触发
    /// </summary>
    public struct ActivityModuleReadyEvent
    {
        /// <summary>
        /// 当前进行中的活动数量
        /// </summary>
        public int ActiveCount;

        /// <summary>
        /// 新开启的活动数量
        /// </summary>
        public int NewlyOpenedCount;
    }

    /// <summary>
    /// 活动注册完成事件
    /// 当业务层完成活动注册后触发
    /// </summary>
    public struct ActivityRegisteredEvent
    {
        /// <summary>
        /// 注册的活动数量
        /// </summary>
        public int RegisteredCount;
    }
}
