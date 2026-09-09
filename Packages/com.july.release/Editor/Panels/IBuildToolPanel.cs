namespace July.Release.Editor
{
    /// <summary>
    /// 构建工具面板接口。每个实现类负责一个独立的 UI 区域，
    /// 通过 <see cref="BuildToolContext"/> 与其他面板共享数据。
    /// </summary>
    public interface IBuildToolPanel
    {
        void OnEnable(BuildToolContext ctx);
        void OnGUI();
    }
}
