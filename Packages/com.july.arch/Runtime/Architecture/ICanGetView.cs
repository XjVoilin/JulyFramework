namespace July.Arch
{
    /// <summary>
    /// 能力接口：按类型获取可定位的场景 View（实现 ISingletonView 的 GameView）。
    /// 实现此接口后可使用 this.GetView&lt;T&gt;() 扩展方法。
    /// </summary>
    public interface ICanGetView : IArchNode { }
}
