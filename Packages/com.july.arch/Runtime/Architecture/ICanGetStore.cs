namespace July.Arch
{
    /// <summary>
    /// 能力接口：获取 Store；数据访问和修改遵守具体 Store 的契约。
    /// 实现此接口后可使用 this.GetStore&lt;T&gt;() 扩展方法。
    /// </summary>
    public interface ICanGetStore : IArchNode { }
}
