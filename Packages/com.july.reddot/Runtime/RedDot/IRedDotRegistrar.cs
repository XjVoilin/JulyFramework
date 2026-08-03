namespace July.RedDot
{
    /// <summary>将一个业务功能的红点节点和 Handler 注册到统一构建流程。</summary>
    public interface IRedDotRegistrar
    {
        void Register(RedDotBuilder builder);
    }
}
