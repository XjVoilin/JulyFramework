namespace July.Arch
{
    /// <summary>
    /// 能力接口：通过 Procedure 执行一次业务操作。
    /// 实现此接口后可使用 this.RunProcedure() 扩展方法并等待执行完成。
    /// 需要共享取消范围时显式传入令牌，业务恢复行为由流程约定。
    /// </summary>
    public interface ICanRunProcedure : IArchNode { }
}
