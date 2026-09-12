using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Arch
{
    /// <summary>
    /// 长期运行能力的统一基类。初始化完成表示该 System 已可供后续 System 使用。
    /// System 管理业务过程及其运行数据的生命周期，可持有普通数据对象并提供查询入口。
    /// 复杂逻辑可委托普通类处理，无需把运行数据统一放入 Store。
    /// </summary>
    public abstract class SystemBase : ICanGetStore, ICanEvent, ICanGetSystem, ICanGetView, ICanRunProcedure
    {
        private ArchContext _architecture;
        private bool _initialized;

        internal bool IsInitialized => _initialized;
        protected CancellationToken InitializationToken { get; private set; }

        internal void SetContext(ArchContext context) => _architecture = context;

        internal async UniTask InitializeAsync(CancellationToken ct = default)
        {
            if (_initialized) return;
            ct.ThrowIfCancellationRequested();
            InitializationToken = ct;
            try
            {
                await OnInitializeAsync();
                _initialized = true;
            }
            finally { InitializationToken = default; }
        }

        internal void Shutdown()
        {
            if (!_initialized) return;

            try { _architecture?.Event?.UnsubscribeAll(this); }
            catch { }

            OnShutdown();
            _initialized = false;
        }

        protected virtual UniTask OnInitializeAsync() => UniTask.CompletedTask;
        protected virtual void OnShutdown() { }

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

        protected T TryGetSystem<T>() where T : class
            => _architecture.TryGetSystem<T>();

        protected T GetView<T>() where T : GameView
            => _architecture.GetView<T>();

        protected UniTask RunProcedure(ProcedureBase procedure, CancellationToken ct = default)
            => _architecture.RunProcedure(procedure, ct);
    }
}
