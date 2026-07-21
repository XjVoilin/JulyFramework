namespace July.Arch
{
    /// <summary>
    /// 能力接口：事件订阅与发布。
    /// 实现此接口后可使用 Subscribe / Unsubscribe / UnsubscribeAll / Publish 扩展方法。
    /// </summary>
    public interface ICanEvent : IArchNode { }
}
