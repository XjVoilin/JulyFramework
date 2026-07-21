using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;

namespace July.Arch
{
    /// <summary>
    /// Store 非泛型基类 — 作为 GetStore 泛型约束和 ArchContext 内部管理的公共类型。
    /// 生命周期方法全部 internal，仅 ArchContext 可调用。
    /// </summary>
    public abstract class StoreBase
    {
        private ArchContext _architecture;
        private bool _initialized;

        internal bool IsInitialized => _initialized;

        internal void SetContext(ArchContext ctx) => _architecture = ctx;

        internal async UniTask InitializeAsync()
        {
            if (_initialized) return;
            await OnInitializeAsync();
            _initialized = true;
        }

        internal void Shutdown()
        {
            if (!_initialized) return;
            OnShutdown();
            _initialized = false;
        }

        protected virtual UniTask OnInitializeAsync() => UniTask.CompletedTask;
        protected virtual void OnShutdown() { }

        /// <summary>
        /// 预留的 Store 写操作追踪钩子。
        /// 当前为空方法，保留签名以便未来接入 debug 追踪。
        /// </summary>
        protected void TraceModify([CallerMemberName] string method = null) { }
    }

    /// <summary>
    /// Store 泛型基类 — 所有业务数据的唯一所有者。
    /// 只要有第二个类需要读这份数据，它就进 Store；System 私有的内部数据除外。
    /// </summary>
    public abstract class StoreBase<TData> : StoreBase where TData : class, new()
    {
        protected TData Data { get; set; }

        protected override UniTask OnInitializeAsync()
        {
            Data = new TData();
            return UniTask.CompletedTask;
        }

        protected void Publish<T>(T eventData)
            => ArchContext.Current.Event.Publish(eventData);
    }
}
