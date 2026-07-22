using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using July.Arch;
using July.Logging;

namespace July.Pooling
{
    public class PoolSystem : SystemBase, IPoolSystem
    {
        private readonly ConcurrentDictionary<Type, IManagedPool> _pools = new();

        protected override void OnShutdown()
        {
            DestroyAllPools();
        }

        public IObjectPool<T> CreatePool<T>(
            Func<T> createFunc = null,
            Action<T> onGet = null,
            Action<T> onReturn = null,
            Action<T> onDestroy = null,
            int initialSize = 0,
            int maxSize = 0) where T : class
        {
            var key = typeof(T);

            if (_pools.TryGetValue(key, out var existing))
            {
                JLogger.LogWarning($"[PoolSystem] 对象池已存在: {key.FullName}，将返回现有池");
                return (IObjectPool<T>)existing;
            }

            var pool = new ObjectPool<T>(createFunc ?? CreateDefault<T>,
                onGet, onReturn, onDestroy, maxSize);

            if (initialSize > 0)
                pool.Warmup(initialSize);

            if (_pools.TryAdd(key, pool)) return pool;

            pool.Clear();
            return GetPool<T>();
        }

        public IObjectPool<T> GetPool<T>() where T : class
        {
            var key = typeof(T);
            if (_pools.TryGetValue(key, out var pool))
                return pool as IObjectPool<T>;
            return null;
        }

        public void Return<T>(T obj) where T : class
        {
            var pool = GetPool<T>();
            if (pool == null)
            {
                JLogger.LogWarning($"[PoolSystem] {typeof(T).Name} 的池子不存在，不能回收");
                return;
            }
            pool.Return(obj);
        }

        public bool DestroyPool<T>() where T : class
        {
            var key = typeof(T);
            if (_pools.TryRemove(key, out var pool))
            {
                pool.Clear();
                return true;
            }
            return false;
        }

        public void DestroyAllPools()
        {
            var pools = new List<IManagedPool>(_pools.Values);
            _pools.Clear();

            foreach (var pool in pools)
                pool.Clear();
        }

        public Dictionary<string, object> GetPoolStatistics()
        {
            var stats = new Dictionary<string, object>();
            foreach (var kvp in _pools)
            {
                var pool = kvp.Value;
                stats[kvp.Key.FullName ?? kvp.Key.Name] = new
                {
                    pool.AvailableCount,
                    pool.ActiveCount,
                    pool.TotalCount
                };
            }
            return stats;
        }

        private static T CreateDefault<T>() where T : class
        {
            try
            {
                return Activator.CreateInstance<T>();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"[PoolSystem] {typeof(T).FullName} has no usable parameterless constructor. " +
                    "Provide createFunc when creating the pool.", exception);
            }
        }

        private interface IManagedPool
        {
            void Clear();
            int AvailableCount { get; }
            int ActiveCount { get; }
            int TotalCount { get; }
        }

        private sealed class ObjectPool<T> : IObjectPool<T>, IManagedPool where T : class
        {
            private readonly Queue<T> _pool = new();
            private readonly HashSet<T> _activeObjects = new();
            private readonly Func<T> _createFunc;
            private readonly Action<T> _onGet;
            private readonly Action<T> _onReturn;
            private readonly Action<T> _onDestroy;
            private readonly int _maxSize;
            private readonly object _lock = new();

            public int AvailableCount { get { lock (_lock) return _pool.Count; } }
            public int ActiveCount { get { lock (_lock) return _activeObjects.Count; } }
            public int TotalCount { get { lock (_lock) return _pool.Count + _activeObjects.Count; } }
            public int MaxSize => _maxSize;

            public ObjectPool(Func<T> createFunc, Action<T> onGet, Action<T> onReturn, Action<T> onDestroy, int maxSize)
            {
                _createFunc = createFunc;
                _onGet = onGet;
                _onReturn = onReturn;
                _onDestroy = onDestroy;
                _maxSize = maxSize;
            }

            public T Get()
            {
                T obj;
                lock (_lock)
                {
                    obj = _pool.Count > 0 ? _pool.Dequeue() : Create();
                    _activeObjects.Add(obj);
                }
                _onGet?.Invoke(obj);
                return obj;
            }

            public void Return(T obj)
            {
                if (obj == null) return;

                var shouldDestroy = false;
                lock (_lock)
                {
                    if (!_activeObjects.Remove(obj)) return;

                    if (_maxSize > 0 && _pool.Count >= _maxSize)
                        shouldDestroy = true;
                    else
                        _pool.Enqueue(obj);
                }

                if (shouldDestroy)
                    _onDestroy?.Invoke(obj);
                else
                    _onReturn?.Invoke(obj);
            }

            public void Clear()
            {
                List<T> allObjects;
                lock (_lock)
                {
                    allObjects = new List<T>(_pool);
                    allObjects.AddRange(_activeObjects);
                    _pool.Clear();
                    _activeObjects.Clear();
                }

                foreach (var obj in allObjects)
                    _onDestroy?.Invoke(obj);
            }

            public void Warmup(int count)
            {
                if (count <= 0) return;

                lock (_lock)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (_maxSize > 0 && _pool.Count >= _maxSize) break;
                        _pool.Enqueue(Create());
                    }
                }
            }

            private T Create()
            {
                var instance = _createFunc();
                if (instance == null)
                    throw new InvalidOperationException(
                        $"[PoolSystem] Factory for {typeof(T).FullName} returned null.");
                return instance;
            }
        }
    }
}
