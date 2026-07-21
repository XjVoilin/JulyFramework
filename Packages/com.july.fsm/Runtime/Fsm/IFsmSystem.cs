using System.Collections.Generic;

namespace July.Fsm
{
    /// <summary>
    /// 状态机系统接口 — 创建/销毁 FSM 实例。
    /// 通过 Scope.GetSystem&lt;IFsmSystem&gt;() 获取。
    /// </summary>
    public interface IFsmSystem
    {
        IFsm CreateFsm(object owner, Dictionary<int, IFsmState> states, int defaultState);
        void DestroyFsm(IFsm fsm);
        void DestroyAllFsms();
    }
}
