using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using July.Arch;
using July.Logging;
using UnityEngine;

namespace July.Pooling
{
    public class PoolSystem : SystemBase, IPoolSystem
    {
        private readonly ConcurrentDictionary<string, object> _pools = new();

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
            var key = typeof(T).FullName;

            if (_pools.ContainsKey(key))
            {
                JLogger.LogWarning($"[PoolSystem] 对象池已存在: {key}，将返回现有池");
                return GetPool<T>();
            }

            var pool = new ObjectPool<T>(createFunc, onGet, onReturn, onDestroy, maxSize);

            if (initialSize > 0)
                pool.Warmup(initialSize);

            _pools[key] = pool;
            return pool;
        }

        public IObjectPool<T> GetPool<T>() where T : class
        {
            var key = typeof(T).FullName;
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
            var key = typeof(T).FullName;
            if (_pools.TryRemove(key, out var pool))
            {
                if (pool is IObjectPool<T> typedPool)
                    typedPool.Clear();
                return true;
            }
            return false;
        }

        public void DestroyAllPools()
        {
            var pools = new List<object>(_pools.Values);
            _pools.Clear();

            foreach (var pool in pools)
            {
                if (pool is IObjectPool<object> objPool)
                    objPool.Clear();
            }
        }

        public Dictionary<string, object> GetPoolStatistics()
        {
            var stats = new Dictionary<string, object>();
            foreach (var kvp in _pools)
            {
                if (kvp.Value is IObjectPool<object> objPool)
                {
                    stats[kvp.Key] = new
                    {
                        objPool.AvailableCount,
                        objPool.ActiveCount,
                        objPool.TotalCount
                    };
                }
            }
            return stats;
        }

        private sealed class ObjectPool<T> : IObjectPool<T> where T : class
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
                    obj = _pool.Count > 0 ? _pool.Dequeue() : _createFunc();
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
                        _pool.Enqueue(_createFunc());
                    }
                }
            }
        }
    }
}
