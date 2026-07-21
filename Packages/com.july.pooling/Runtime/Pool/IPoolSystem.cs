using System;
using System.Collections.Generic;

namespace July.Pooling
{
    public interface IPoolSystem
    {
        IObjectPool<T> CreatePool<T>(
            Func<T> createFunc = null,
            Action<T> onGet = null,
            Action<T> onReturn = null,
            Action<T> onDestroy = null,
            int initialSize = 0,
            int maxSize = 0) where T : class;

        IObjectPool<T> GetPool<T>() where T : class;
        void Return<T>(T obj) where T : class;
        bool DestroyPool<T>() where T : class;
        void DestroyAllPools();
        Dictionary<string, object> GetPoolStatistics();
    }
}
