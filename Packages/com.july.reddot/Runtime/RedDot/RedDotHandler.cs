using July.Arch;

namespace July.RedDot
{
    /// <summary>
    /// 红点计算器绑定基类。
    /// 子类定义 Key、计算逻辑和事件订阅，生命周期由 RedDotSystemBase 管理。
    /// </summary>
    public abstract class RedDotHandler : ICanEvent, ICanGetSystem
    {
        private RedDotSystemBase _system;

        protected abstract string Key { get; }
        protected abstract int ComputeCount();
        protected virtual void OnSubscribeEvents() { }

        internal void Attach(RedDotSystemBase system)
        {
            _system = system;
            _system.SetCalculator(Key, _ => ComputeCount());
            OnSubscribeEvents();
        }

        internal void Detach()
        {
            this.UnsubscribeAll();
            _system?.RemoveCalculator(Key);
            _system = null;
        }

        protected void RaiseChanged()
        {
            _system?.Refresh(Key);
        }
    }
}
