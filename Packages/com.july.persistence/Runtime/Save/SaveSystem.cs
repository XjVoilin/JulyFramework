using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;
using UnityEngine;

namespace July.Persistence
{
    /// <summary>
    /// SaveSystem 抽象基类 — 注册 / 脏标记 / 调度 / 序列化管线。
    /// 子类只需实现 IO 层：WriteDataAsync / ReadDataAsync / DataExists / DeleteData / GetSavePath。
    /// </summary>
    public abstract class SaveSystem : SystemBase, ISaveSystem, IUpdatableSystem
    {
        #region Constants

        private const float AutoSaveInterval = 30f;
        private const int MediumDirtyCount = 3;

        #endregion

        #region Fields

        private ISaveStrategy _saveStrategy;
        private float _lastAutoSaveTime;
        private bool _isSaving;

        private readonly Dictionary<string, ISaveData> _registered = new();
        private readonly HashSet<string> _dirtyKeys = new();
        private readonly object _lock = new();

        private ISerializeSystem _serializeSystem;
        private IEncryptionSystem _encryptionSystem;

        #endregion

        #region Abstract IO — 子类实现

        protected abstract UniTask<bool> WriteDataAsync(string key, byte[] data, CancellationToken ct);
        protected abstract UniTask<byte[]> ReadDataAsync(string key, CancellationToken ct);
        protected abstract bool DataExists(string key);
        protected abstract bool DeleteData(string key);

        /// <summary>
        /// 返回存档的物理路径或逻辑键名（PlayerPrefs 可返回 key 本身）。
        /// </summary>
        public abstract string GetSavePath(string key);

        #endregion

        #region Virtual — 子类可选覆写

        protected virtual UniTask<SaveResult> SaveWithRetryAsync(string key, byte[] processedData,
            CancellationToken ct)
        {
            return WriteDataAsync(key, processedData, ct)
                .ContinueWith(success => success
                    ? SaveResult.CreateSuccess()
                    : SaveResult.CreateFailure(SaveFailureReason.Unknown));
        }

        #endregion

        #region Save Format Constants

        protected const byte CurrentSaveVersion = 1;

        #endregion

        #region Lifecycle

        protected override UniTask OnInitializeAsync()
        {
            _saveStrategy = new ImportanceBasedSaveStrategy();
            _lastAutoSaveTime = 0f;
            _isSaving = false;
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown()
        {
            lock (_lock)
            {
                _dirtyKeys.Clear();
                _registered.Clear();
            }
        }

        public void OnUpdate(float deltaTime)
        {
            _lastAutoSaveTime += deltaTime;
            if (_lastAutoSaveTime >= AutoSaveInterval && !_isSaving)
            {
                int dirtyCount;
                lock (_lock) { dirtyCount = _dirtyKeys.Count; }

                if (dirtyCount > 0)
                {
                    _lastAutoSaveTime = 0f;
                    _isSaving = true;
                    TriggerSaveAsync(SaveSignal.Low)
                        .ContinueWith(_ => { _isSaving = false; })
                        .Forget();
                }
            }
        }

        protected ISerializeSystem GetSerializeSystem()
        {
            return _serializeSystem ??= this.TryGetSystem<ISerializeSystem>();
        }

        protected IEncryptionSystem GetEncryptionSystem()
        {
            return _encryptionSystem ??= this.TryGetSystem<IEncryptionSystem>();
        }

        #endregion

        #region Strategy

        public void SetPolicy(ISaveStrategy strategy)
        {
            if (strategy != null) _saveStrategy = strategy;
        }

        public ISaveStrategy GetPolicy() => _saveStrategy;

        #endregion

        #region Registration

        public void Register(string key, ISaveData data)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key), "存档键不能为空");
            if (data == null)
                throw new ArgumentNullException(nameof(data), "存档数据不能为空");

            lock (_lock)
            {
                if (_registered.ContainsKey(key))
                    JLogger.LogWarning($"[SaveSystem] 存档数据已注册，将覆盖: {key}");

                _registered[key] = data;
            }
        }

        public bool Unregister(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            lock (_lock)
            {
                _dirtyKeys.Remove(key);
                return _registered.Remove(key);
            }
        }

        public bool IsRegistered(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            lock (_lock)
            {
                return _registered.ContainsKey(key);
            }
        }

        public T GetRegisteredData<T>(string key) where T : class, ISaveData
        {
            if (string.IsNullOrEmpty(key)) return null;

            lock (_lock)
            {
                return _registered.TryGetValue(key, out var data) ? data as T : null;
            }
        }

        public IEnumerable<string> GetAllRegisteredKeys()
        {
            lock (_lock)
            {
                return _registered.Keys.ToList();
            }
        }

        #endregion

        #region Dirty Tracking

        public bool MarkDirty(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            lock (_lock)
            {
                if (!_registered.ContainsKey(key))
                {
                    JLogger.LogWarning($"[SaveSystem] 无法标记脏数据，数据未注册: {key}");
                    return false;
                }

                _dirtyKeys.Add(key);
                return true;
            }
        }

        public bool IsDirty(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            lock (_lock)
            {
                return _dirtyKeys.Contains(key);
            }
        }

        public int DirtyCount
        {
            get
            {
                lock (_lock) { return _dirtyKeys.Count; }
            }
        }

        public IEnumerable<string> GetDirtyKeys()
        {
            lock (_lock)
            {
                return _dirtyKeys.ToList();
            }
        }

        public void ClearDirty(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            lock (_lock)
            {
                _dirtyKeys.Remove(key);
            }
        }

        public void ClearAllDirty()
        {
            lock (_lock)
            {
                _dirtyKeys.Clear();
            }
        }

        #endregion

        #region Save Trigger

        public async UniTask<Dictionary<string, SaveResult>> TriggerSaveAsync(SaveSignal signal)
        {
            var keysToSave = GetKeysToSave(signal);
            if (keysToSave.Count == 0)
                return new Dictionary<string, SaveResult>();

            var results = new Dictionary<string, SaveResult>();
            foreach (var key in keysToSave)
            {
                ISaveData data;
                lock (_lock)
                {
                    if (!_registered.TryGetValue(key, out data)) continue;
                }

                var result = await SaveInternalAsync(key, data, default);
                results[key] = result;

                if (result.Success)
                    ClearDirty(key);
            }

            return results;
        }

        public async UniTask<bool> MarkDirtyAndSaveAsync(string key, SaveSignal signal)
        {
            if (!MarkDirty(key)) return false;

            switch (signal)
            {
                case SaveSignal.Low:
                    return true;

                case SaveSignal.Medium:
                    if (DirtyCount < MediumDirtyCount)
                        return true;
                    var results = await TriggerSaveAsync(signal);
                    return results.TryGetValue(key, out var result) && result.Success;

                case SaveSignal.High:
                case SaveSignal.Immediate:
                    var saveResults = await TriggerSaveAsync(signal);
                    return saveResults.TryGetValue(key, out var saveResult) && saveResult.Success;

                default:
                    return true;
            }
        }

        private List<string> GetKeysToSave(SaveSignal signal)
        {
            var result = new List<string>();
            lock (_lock)
            {
                foreach (var key in _dirtyKeys)
                {
                    if (!_registered.TryGetValue(key, out var data)) continue;
                    var context = new SaveContext(signal, key, data);
                    if (_saveStrategy.ShouldSave(context))
                        result.Add(key);
                }
            }
            return result;
        }

        #endregion

        #region Save / Load (Public API)

        public async UniTask<SaveResult> SaveAsync<T>(string key, T data, CancellationToken ct = default)
            where T : ISaveData
        {
            if (string.IsNullOrEmpty(key))
                return SaveResult.CreateFailure(SaveFailureReason.InvalidData, "存档key不能为空");

            return await SaveInternalAsync(key, data, ct);
        }

        public async UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default)
            where T : ISaveData, new()
        {
            return await LoadInternalAsync<T>(key, ct);
        }

        public async UniTask<T> LoadAndRegisterAsync<T>(string key, CancellationToken ct = default)
            where T : ISaveData, new()
        {
            T data;
            if (HasSave(key))
            {
                data = await LoadInternalAsync<T>(key, ct);
                if (data == null)
                    data = new T();
            }
            else
            {
                data = new T();
            }

            Register(key, data);
            return data;
        }

        public async UniTask<Dictionary<string, T>> LoadAndRegisterBatchAsync<T>(
            string[] keys, CancellationToken ct = default) where T : ISaveData, new()
        {
            var results = new Dictionary<string, T>(keys.Length);
            foreach (var key in keys)
            {
                if (ct.IsCancellationRequested) break;
                var data = await LoadAndRegisterAsync<T>(key, ct);
                results[key] = data;
            }
            return results;
        }

        public async UniTask<Dictionary<string, SaveResult>> SaveBatchAsync<T>(
            Dictionary<string, T> dataMap, CancellationToken ct = default) where T : ISaveData
        {
            var results = new Dictionary<string, SaveResult>(dataMap.Count);
            foreach (var kvp in dataMap)
            {
                if (ct.IsCancellationRequested)
                {
                    results[kvp.Key] = SaveResult.CreateFailure(SaveFailureReason.Cancelled);
                    continue;
                }

                results[kvp.Key] = await SaveAsync(kvp.Key, kvp.Value, ct);
            }
            return results;
        }

        public async UniTask<Dictionary<string, T>> LoadBatchAsync<T>(
            string[] keys, CancellationToken ct = default) where T : ISaveData, new()
        {
            var results = new Dictionary<string, T>(keys.Length);
            foreach (var key in keys)
            {
                if (ct.IsCancellationRequested) break;
                var data = await LoadInternalAsync<T>(key, ct);
                if (data != null)
                    results[key] = data;
            }
            return results;
        }

        #endregion

        #region Delete / HasSave

        public bool Delete(string key)
        {
            Unregister(key);

            try
            {
                if (string.IsNullOrEmpty(key)) return false;
                return DeleteData(key);
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[SaveSystem] 删除失败: {key}, 错误: {ex.Message}");
                return false;
            }
        }

        public bool HasSave(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return DataExists(key);
        }

        #endregion

        #region Internal Save / Load Pipeline

        private async UniTask<SaveResult> SaveInternalAsync(string key, ISaveData data, CancellationToken ct)
        {
            var (processedData, failureReason) = ProcessBeforeSave(data, key);
            if (processedData == null)
                return SaveResult.CreateFailure(failureReason ?? SaveFailureReason.Unknown);

            return await SaveWithRetryAsync(key, processedData, ct);
        }

        private async UniTask<T> LoadInternalAsync<T>(string key, CancellationToken ct) where T : ISaveData
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    JLogger.LogWarning("[SaveSystem] 存档key不能为空");
                    return default;
                }

                if (!DataExists(key))
                    return default;

                var rawBytes = await ReadDataAsync(key, ct);
                if (rawBytes == null || rawBytes.Length == 0)
                {
                    JLogger.LogWarning($"[SaveSystem] 存档数据为空: {key}");
                    return default;
                }

                return ProcessAfterLoad<T>(rawBytes, key);
            }
            catch (OperationCanceledException)
            {
                JLogger.LogWarning($"[SaveSystem] 加载操作已取消: {key}");
                return default;
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[SaveSystem] 加载失败: {key}, 错误: {ex.Message}");
                return default;
            }
        }

        #endregion

        #region Serialize + Encrypt Pipeline

        private (byte[] data, SaveFailureReason? failureReason) ProcessBeforeSave<T>(T data, string key)
            where T : ISaveData
        {
            if (data == null)
                return (null, SaveFailureReason.InvalidData);

            var serializeSystem = GetSerializeSystem();
            if (serializeSystem == null)
            {
                JLogger.LogError("[SaveSystem] ISerializeSystem 未注册，无法保存");
                return (null, SaveFailureReason.SerializationFailed);
            }

            byte[] bytes;
            try
            {
                bytes = serializeSystem.Serialize(data);
                if (bytes == null || bytes.Length == 0)
                {
                    JLogger.LogWarning($"[SaveSystem] 序列化数据为空: {key}");
                    return (null, SaveFailureReason.SerializationFailed);
                }
            }
            catch (Exception ex)
            {
                JLogger.LogException(ex);
                return (null, SaveFailureReason.SerializationFailed);
            }

            var encryptionSystem = GetEncryptionSystem();
            if (encryptionSystem != null)
            {
                try
                {
                    var encryptedBytes = encryptionSystem.Encrypt(bytes);
                    if (encryptedBytes == null || encryptedBytes.Length == 0)
                    {
                        JLogger.LogError($"[SaveSystem] 加密失败: {key}");
                        return (null, SaveFailureReason.EncryptionFailed);
                    }

                    bytes = encryptedBytes;
                }
                catch (Exception ex)
                {
                    JLogger.LogException(ex);
                    return (null, SaveFailureReason.EncryptionFailed);
                }
            }

            return (CreateSaveData(bytes), null);
        }

        protected T ProcessAfterLoad<T>(byte[] rawBytes, string key) where T : ISaveData
        {
            if (rawBytes == null || rawBytes.Length == 0)
                return default;

            var bytes = ParseSaveData(rawBytes, key);
            if (bytes == null || bytes.Length == 0)
                return default;

            var encryptionSystem = GetEncryptionSystem();
            if (encryptionSystem != null)
            {
                var decryptedBytes = encryptionSystem.Decrypt(bytes);
                if (decryptedBytes == null || decryptedBytes.Length == 0)
                {
                    JLogger.LogError($"[SaveSystem] 解密失败: {key}");
                    return default;
                }

                bytes = decryptedBytes;
            }

            var serializeSystem = GetSerializeSystem();
            if (serializeSystem == null)
            {
                JLogger.LogError("[SaveSystem] ISerializeSystem 未注册，无法加载");
                return default;
            }

            return serializeSystem.Deserialize<T>(bytes);
        }

        protected static byte[] CreateSaveData(byte[] encryptedData)
        {
            const int headerSize = 5;
            var result = new byte[headerSize + encryptedData.Length];
            var offset = 0;

            result[offset++] = CurrentSaveVersion;

            var lengthBytes = BitConverter.GetBytes(encryptedData.Length);
            Array.Copy(lengthBytes, 0, result, offset, 4);
            offset += 4;

            Array.Copy(encryptedData, 0, result, offset, encryptedData.Length);
            return result;
        }

        protected byte[] ParseSaveData(byte[] rawData, string key)
        {
            if (rawData.Length < 5)
            {
                JLogger.LogError($"[SaveSystem] 存档数据格式无效（长度不足）: {key}");
                return null;
            }

            var offset = 0;
            var version = rawData[offset++];

            if (version != CurrentSaveVersion)
            {
                JLogger.LogError(
                    $"[SaveSystem] 存档版本 {version} 不受支持（当前: {CurrentSaveVersion}）, key: {key}");
                return null;
            }

            var dataLength = BitConverter.ToInt32(rawData, offset);
            offset += 4;

            if (dataLength < 0 || offset + dataLength > rawData.Length)
            {
                JLogger.LogError($"[SaveSystem] 存档数据长度无效: {dataLength}, key: {key}");
                return null;
            }

            var data = new byte[dataLength];
            Array.Copy(rawData, offset, data, 0, dataLength);
            return data;
        }

        #endregion
    }
}
