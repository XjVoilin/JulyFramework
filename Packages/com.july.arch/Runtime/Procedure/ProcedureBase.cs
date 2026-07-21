using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Arch
{
    /// <summary>
    /// 长流程编排原语。可 await / cancel / 嵌套；实例一次性，每次 new。
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

        protected UniTask RunProcedure(ProcedureBase procedure, CancellationToken ct = default)
            => _architecture.RunProcedure(procedure, ct);
    }
}
