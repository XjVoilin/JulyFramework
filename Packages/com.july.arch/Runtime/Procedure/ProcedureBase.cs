using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Arch
{
    /// <summary>
    /// 一次业务操作的执行编排。
    /// 在声明的职责范围内组织规则调用、状态变更、异步等待和表现交接。
    /// 权威状态由 System、Store 及其内部模型持有；本实例保存执行上下文和结果。
    /// 子流程取消令牌须显式传递；完成、拒绝、失败及取消的处理由流程约定。
    /// 实例按单次执行使用，执行结束后框架解除其所属事件订阅。
    /// </summary>
    public abstract class ProcedureBase : ICanGetStore, ICanEvent, ICanGetSystem, ICanGetView, ICanRunProcedure
    {
        private ArchContext _architecture;

        internal void SetContext(ArchContext ctx) => _architecture = ctx;

        internal UniTask Execute(CancellationToken ct) => OnExecuteAsync(ct);

        protected abstract UniTask OnExecuteAsync(CancellationToken ct);

        protected T GetStore<T>() where T : StoreBase
            => _architecture.GetStore<T>();

        protected void Subscribe<T>(Action<T> handler)
            => _architecture.Event.Subscribe(handler, this);

        protected void Unsubscribe<T>(Action<T> handler)
            => _architecture.Event.Unsubscribe(handler);

        protected void Publish<T>(T eventData)
            => _architecture.Event.Publish(eventData);

        protected T GetSystem<T>() where T : class
            => _architecture.GetSystem<T>();

        protected T GetView<T>() where T : GameView
            => _architecture.GetView<T>();

        /// <summary>执行子流程；需要共享取消范围时显式传入令牌。</summary>
        protected UniTask RunProcedure(ProcedureBase procedure, CancellationToken ct = default)
            => _architecture.RunProcedure(procedure, ct);
    }
}
