namespace July.Arch
{
    /// <summary>
    /// 可定位性标记接口（空标记，无任何方法）。
    /// GameView 子类实现此接口即声明"我是一个按类型唯一、可被 GetView&lt;T&gt;() 定位的 View"。
    /// 注册动作由 GameView 基类在 Awake 中通过类型判断自动完成，业务侧无需任何注册代码。
    /// 与能力维度（ICanGetStore / ICanEvent / ICanGetSystem）正交：是否实现此接口与是否使用框架能力无关。
    /// 多实例对象（如棋子、敌人）不应实现此接口。
    /// </summary>
    public interface ISingletonView { }
}
