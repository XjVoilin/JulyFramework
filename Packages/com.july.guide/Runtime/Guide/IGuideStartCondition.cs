namespace July.Guide
{
    /// <summary>
    /// 项目为引导计划组装的开始条件，参数和依赖由条件实现负责。
    /// </summary>
    public interface IGuideStartCondition
    {
        /// <summary>
        /// 在 Unity 主线程根据当前业务状态同步判断计划能否开始。判断必须无副作用。
        /// </summary>
        bool CanStart();
    }
}
