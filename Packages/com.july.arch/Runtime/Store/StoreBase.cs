using System;

namespace July.Arch
{
    /// <summary>
    /// Store 的非泛型基类，由 ArchContext 按具体类型管理。
    /// Store 只负责持有领域状态，不参与异步生命周期，也不知道数据来自本地还是服务器。
    /// </summary>
    public abstract class StoreBase
    {
        private ArchContext _architecture;

        internal void SetContext(ArchContext context) => _architecture = context;

        protected void Publish<T>(T eventData)
            => _architecture.Event.Publish(eventData);
    }

    /// <summary>
    /// 领域状态的统一所有者。完整数据可以由外部替换，局部修改由具体 Store 封装。
    /// </summary>
    public abstract class StoreBase<TData> : StoreBase where TData : class, new()
    {
        private TData _data = new TData();

        /// <summary>
        /// 当前领域数据。
        /// </summary>
        protected TData Data => _data;

        /// <summary>
        /// 获取当前完整数据，供数据传输和持久化模块读取。
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
