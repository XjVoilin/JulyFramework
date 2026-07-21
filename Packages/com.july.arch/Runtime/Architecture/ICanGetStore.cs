namespace July.Arch
{
    /// <summary>
    /// 能力接口：获取 Store 数据接口（读属性 + 写方法）。
    /// 实现此接口后可使用 this.GetStore&lt;T&gt;() 扩展方法。
    /// </summary>
    public interface ICanGetStore : IArchNode { }
}
