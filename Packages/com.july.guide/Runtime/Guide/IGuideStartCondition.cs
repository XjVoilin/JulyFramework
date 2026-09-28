namespace July.Guide
{
    /// <summary>
    /// 项目提供的开始条件策略。框架按条件类型注册，同类型的引导共用一个实例。
    /// 只读取参数配置和最新业务状态，不保存某次引导的执行进度。
    /// </summary>
    public interface IGuideStartCondition
    {
        /// <summary>项目定义的稳定条件类型编号；不是引导编号，实例存续期间保持不变。</summary>
        int Type { get; }

        /// <summary>
        /// 在 Unity 主线程同步判断指定参数配置是否允许开始。
        /// paramId 是本策略解释的配置记录编号，不是任意参数值；配置引用由项目保证有效。
        /// 无参数策略可约定使用 0。不修改业务状态、不启动引导或异步工作。
        /// </summary>
        bool CanStart(int paramId);
    }
}
