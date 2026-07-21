using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Persistence
{
    public interface ISaveSystem
    {
        void SetPolicy(ISaveStrategy strategy);
        ISaveStrategy GetPolicy();

        void Register(string key, ISaveData data);
        bool Unregister(string key);
        bool IsRegistered(string key);
        T GetRegisteredData<T>(string key) where T : class, ISaveData;
        IEnumerable<string> GetAllRegisteredKeys();

        bool MarkDirty(string key);
        bool IsDirty(string key);
        int DirtyCount { get; }
        IEnumerable<string> GetDirtyKeys();
        void ClearDirty(string key);
        void ClearAllDirty();

        UniTask<Dictionary<string, SaveResult>> TriggerSaveAsync(SaveSignal signal);
        UniTask<bool> MarkDirtyAndSaveAsync(string key, SaveSignal signal);

        UniTask<T> LoadAndRegisterAsync<T>(string key, CancellationToken ct = default)
            where T : ISaveData, new();
        UniTask<Dictionary<string, T>> LoadAndRegisterBatchAsync<T>(string[] keys, CancellationToken ct = default)
            where T : ISaveData, new();
        UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default)
            where T : ISaveData, new();

        UniTask<SaveResult> SaveAsync<T>(string key, T data, CancellationToken ct = default)
            where T : ISaveData;

        UniTask<Dictionary<string, SaveResult>> SaveBatchAsync<T>(
            Dictionary<string, T> dataMap, CancellationToken ct = default) where T : ISaveData;
        UniTask<Dictionary<string, T>> LoadBatchAsync<T>(
            string[] keys, CancellationToken ct = default) where T : ISaveData, new();

        bool Delete(string key);
        bool HasSave(string key);
        string GetSavePath(string key);
    }
}
