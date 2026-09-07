using System;

namespace July.Arch
{
    /// <summary>
    /// Store 的非泛型基类，由 ArchContext 按具体类型管理。
    /// Store 管理需要独立维护的业务数据，默认用于长期玩家数据；不限制数据生命周期。
    /// 不参与异步生命周期，也不决定数据来自本地还是服务器。
    /// </summary>
    public abstract class StoreBase
    {
        private ArchContext _architecture;

        internal void SetContext(ArchContext context) => _architecture = context;

        protected void Publish<T>(T eventData)
            => _architecture.Event.Publish(eventData);
    }

    /// <summary>
    /// 当前 TData 及其关联数据的所有者，不要求所有模块运行数据都放入 Store。
    /// 完整数据可以由外部替换；简单数据可由 System 修改并按具体 Store 契约通知，
    /// 涉及关联、索引或缓存的修改由 Store 或其数据对象提供完整操作。
    /// </summary>
    public abstract class StoreBase<TData> : StoreBase where TData : class, new()
    {
        private TData _data = new TData();

        /// <summary>
        /// 当前领域数据。
        /// </summary>
        protected TData Data => _data;

        /// <summary>
        /// 获取当前完整数据的可变引用，用于业务读取、数据传输和持久化；不复制数据。
        /// 修改须遵守具体 Store 的一致性与通知契约，直接修改不会自动标脏。
        /// </summary>
        public TData GetData() => Data;

        /// <summary>
        /// 使用同类型数据整体覆盖当前状态，不附带服务器或存档语义。
        /// </summary>
        public void ReplaceData(TData data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            OnDataReplaced();
            MarkDirty();
        }

        /// <summary>
        /// 完整数据被替换后的扩展点，用于重建 Store 自己维护的派生状态。
        /// </summary>
        protected virtual void OnDataReplaced() { }

        /// <summary>
        /// Store 确认数据已修改时发送信号。没有外部监听时不会产生额外行为。
        /// </summary>
        protected void MarkDirty() => DirtyMarked?.Invoke();

        /// <summary>
        /// 数据修改信号。持久化模块只监听项目明确声明需要持久化的 Store。
        /// </summary>
        public event Action DirtyMarked;
    }
}
