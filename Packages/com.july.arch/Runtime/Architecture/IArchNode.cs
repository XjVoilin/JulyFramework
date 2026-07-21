namespace July.Arch
{
    /// <summary>
    /// 架构节点标记接口 —— 所有接入框架的类的唯一标识。
    /// 用于 ArchContext.RegisterSystem 过滤框架接口，防止按 ICanGetStore 等注册 System。
    /// </summary>
    public interface IArchNode { }
}
