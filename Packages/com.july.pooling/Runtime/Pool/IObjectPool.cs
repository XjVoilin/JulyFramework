namespace July.Pooling
{
    public interface IObjectPool<T> where T : class
    {
        T Get();
        void Return(T obj);
        void Clear();
        void Warmup(int count);
        int AvailableCount { get; }
        int ActiveCount { get; }
        int TotalCount { get; }
        int MaxSize { get; }
    }
}
