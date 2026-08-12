using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;

namespace July.Persistence
{
    /// <summary>
    /// 本地持久化管线：恢复已声明的 Store、跟踪修改，并完成序列化、加密与存储。
    /// Arch、Store 与 SaveSystem 统一遵循 Unity 主线程模型；异步存储操作由内部队列串行执行。
    /// </summary>
    public abstract class SaveSystem : SystemBase, ISaveSystem, IUpdatableSystem
    {
        private const float AutoSaveInterval = 30f;
        private const byte CurrentSaveVersion = 1;

        private readonly List<ISaveEntry> _entries = new();

        private ISerializeSystem _serializeSystem;
        private IEncryptionSystem _encryptionSystem;
        private UniTask _saveTail = UniTask.CompletedTask;
        private float _lastAutoSaveTime;
        private bool _autoSaveRunning;
        private bool _acceptingDeclarations = true;

        protected abstract UniTask<bool> WriteDataAsync(
            string key,
            byte[] data,
            CancellationToken ct);

        protected abstract UniTask<byte[]> ReadDataAsync(string key, CancellationToken ct);
        protected abstract bool DataExists(string key);

        protected virtual UniTask<SaveResult> SaveWithRetryAsync(
            string key,
            byte[] processedData,
            CancellationToken ct)
        {
            return WriteDataAsync(key, processedData, ct)
                .ContinueWith(success => success
                    ? SaveResult.CreateSuccess()
                    : SaveResult.CreateFailure(SaveFailureReason.Unknown));
        }

        /// <summary>
        /// 声明一个 Store 参与本地持久化。异步恢复发生在 SaveSystem 初始化时；
        /// Critical Store 标脏后会立即进入串行写入队列。
        /// </summary>
        public StoreBase<TData> Persist<TData>(
            StoreBase<TData> store,
            string key,
            SaveImportance importance) where TData : class, new()
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            ValidateKey(key);

            if (!_acceptingDeclarations)
                throw new InvalidOperationException("SaveSystem 初始化后不能再声明持久化 Store。");

            foreach (var entry in _entries)
            {
                if (entry.Key == key)
                    throw new InvalidOperationException($"存档 Key 已声明：{key}");
                if (entry.Owns(store))
                    throw new InvalidOperationException($"Store 已声明持久化：{store.GetType().Name}");
            }

            _entries.Add(new StoreSaveEntry<TData>(this, store, key, importance));
            return store;
        }

        protected override async UniTask OnInitializeAsync()
        {
            _acceptingDeclarations = false;
            _serializeSystem = TryGetSystem<ISerializeSystem>();
            if (_serializeSystem == null)
                throw new InvalidOperationException("SaveSystem 需要先注册并初始化 ISerializeSystem。");

            _encryptionSystem = TryGetSystem<IEncryptionSystem>();
            _lastAutoSaveTime = 0f;
            _autoSaveRunning = false;

            var restoreTasks = new UniTask[_entries.Count];
            for (var i = 0; i < _entries.Count; i++)
                restoreTasks[i] = _entries[i].RestoreAsync(CancellationToken.None);

            await UniTask.WhenAll(restoreTasks);

            foreach (var entry in _entries)
                entry.Attach();
        }

        protected override void OnShutdown()
        {
            foreach (var entry in _entries)
                entry.Detach();

            _entries.Clear();
            _serializeSystem = null;
            _encryptionSystem = null;
            _autoSaveRunning = false;
            _acceptingDeclarations = true;
        }

        public void OnUpdate(float deltaTime)
        {
            _lastAutoSaveTime += deltaTime;
            if (_lastAutoSaveTime < AutoSaveInterval || _autoSaveRunning || !HasDirtyEntries())
                return;

            _lastAutoSaveTime = 0f;
            _autoSaveRunning = true;
            AutoSaveAsync().Forget();
        }

        private async UniTask AutoSaveAsync()
        {
            try
            {
                await FlushAsync(SaveSignal.Low);
            }
            finally
            {
                _autoSaveRunning = false;
            }
        }

        public UniTask<IReadOnlyDictionary<string, SaveResult>> FlushAsync(
            SaveSignal signal,
            CancellationToken ct = default)
        {
            return EnqueueSave(() => FlushCoreAsync(signal, ct));
        }

        public UniTask<SaveResult> SaveNowAsync(
            StoreBase store,
            CancellationToken ct = default)
        {
            return EnqueueSave(() => SaveNowCoreAsync(store, ct));
        }

        private async UniTask<IReadOnlyDictionary<string, SaveResult>> FlushCoreAsync(
            SaveSignal signal,
            CancellationToken ct)
        {
            var results = new Dictionary<string, SaveResult>();
            foreach (var entry in _entries)
            {
                if (!entry.IsDirty || !ShouldSave(entry.Importance, signal)) continue;

                ct.ThrowIfCancellationRequested();
                results[entry.Key] = await entry.SaveAsync(ct);
            }

            return results;
        }

        private async UniTask<SaveResult> SaveNowCoreAsync(
            StoreBase store,
            CancellationToken ct)
        {
            if (store == null)
                return SaveResult.CreateFailure(
                    SaveFailureReason.InvalidData,
                    "Store 不能为空。");

            foreach (var entry in _entries)
            {
                if (entry.Owns(store))
                    return await entry.SaveAsync(ct);
            }

            return SaveResult.CreateFailure(
                SaveFailureReason.InvalidData,
                $"Store 未声明持久化：{store.GetType().Name}");
        }

        /// <summary>
        /// 将所有写操作排成单一异步序列，避免同一主线程上的 await 重入造成存档覆盖乱序。
        /// </summary>
        private async UniTask<T> EnqueueSave<T>(Func<UniTask<T>> operation)
        {
            var previous = _saveTail;
            var completion = new UniTaskCompletionSource();
            _saveTail = completion.Task;

            await previous;
            try
            {
                return await operation();
            }
            finally
            {
                completion.TrySetResult();
            }
        }

        private void RequestCriticalSave(ISaveEntry entry)
        {
            SaveCriticalEntryAsync(entry).Forget();
        }

        private async UniTask SaveCriticalEntryAsync(ISaveEntry entry)
        {
            try
            {
                var result = await EnqueueSave(
                    () => entry.SaveAsync(CancellationToken.None));
                if (!result.Success)
                {
                    JLogger.LogWarning(
                        $"[SaveSystem] Critical Store 即时保存失败：{entry.Key}，{result.FailureMessage}");
                }
            }
            catch (Exception exception)
            {
                JLogger.LogException(exception);
            }
        }

        private bool HasDirtyEntries()
        {
            foreach (var entry in _entries)
                if (entry.IsDirty) return true;
            return false;
        }

        private static bool ShouldSave(SaveImportance importance, SaveSignal signal)
        {
            return signal switch
            {
                SaveSignal.Low => importance == SaveImportance.Critical,
                SaveSignal.Medium => importance <= SaveImportance.Important,
                SaveSignal.High => importance <= SaveImportance.Normal,
                SaveSignal.Immediate => true,
                _ => false
            };
        }

        private async UniTask<SaveResult> SaveInternalAsync<T>(
            string key,
            T data,
            CancellationToken ct) where T : class
        {
            var (processedData, failureReason) = ProcessBeforeSave(data, key);
            if (processedData == null)
                return SaveResult.CreateFailure(failureReason ?? SaveFailureReason.Unknown);

            return await SaveWithRetryAsync(key, processedData, ct);
        }

        private async UniTask<T> LoadInternalAsync<T>(
            string key,
            CancellationToken ct) where T : class
        {
            try
            {
                if (!DataExists(key)) return null;

                var rawBytes = await ReadDataAsync(key, ct);
                if (rawBytes == null || rawBytes.Length == 0)
                {
                    JLogger.LogWarning($"[SaveSystem] 存档数据为空：{key}");
                    return null;
                }

                return ProcessAfterLoad<T>(rawBytes, key);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                JLogger.LogError($"[SaveSystem] 加载失败：{key}，错误：{ex.Message}");
                return null;
            }
        }

        private (byte[] data, SaveFailureReason? failureReason) ProcessBeforeSave<T>(
            T data,
            string key) where T : class
        {
            if (data == null)
                return (null, SaveFailureReason.InvalidData);

            byte[] bytes;
            try
            {
                bytes = _serializeSystem.Serialize(data);
                if (bytes == null || bytes.Length == 0)
                {
                    JLogger.LogWarning($"[SaveSystem] 序列化结果为空：{key}");
                    return (null, SaveFailureReason.SerializationFailed);
                }
            }
            catch (Exception ex)
            {
                JLogger.LogException(ex);
                return (null, SaveFailureReason.SerializationFailed);
            }

            if (_encryptionSystem != null)
            {
                try
                {
                    var encryptedBytes = _encryptionSystem.Encrypt(bytes);
                    if (encryptedBytes == null || encryptedBytes.Length == 0)
                    {
                        JLogger.LogError($"[SaveSystem] 加密失败：{key}");
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

        private T ProcessAfterLoad<T>(byte[] rawBytes, string key) where T : class
        {
            var bytes = ParseSaveData(rawBytes, key);
            if (bytes == null || bytes.Length == 0) return null;

            if (_encryptionSystem != null)
            {
                var decryptedBytes = _encryptionSystem.Decrypt(bytes);
                if (decryptedBytes == null || decryptedBytes.Length == 0)
                {
                    JLogger.LogError($"[SaveSystem] 解密失败：{key}");
                    return null;
                }

                bytes = decryptedBytes;
            }

            return _serializeSystem.Deserialize<T>(bytes);
        }

        private static byte[] CreateSaveData(byte[] data)
        {
            const int headerSize = 5;
            var result = new byte[headerSize + data.Length];
            result[0] = CurrentSaveVersion;
            Array.Copy(BitConverter.GetBytes(data.Length), 0, result, 1, 4);
            Array.Copy(data, 0, result, headerSize, data.Length);
            return result;
        }

        private static byte[] ParseSaveData(byte[] rawData, string key)
        {
            if (rawData == null || rawData.Length < 5)
            {
                JLogger.LogError($"[SaveSystem] 存档格式无效（长度不足）：{key}");
                return null;
            }

            var version = rawData[0];
            if (version != CurrentSaveVersion)
            {
                JLogger.LogError(
                    $"[SaveSystem] 不支持存档版本 {version}（当前版本 {CurrentSaveVersion}）：{key}");
                return null;
            }

            var dataLength = BitConverter.ToInt32(rawData, 1);
            const int dataOffset = 5;
            if (dataLength < 0 || dataOffset + dataLength > rawData.Length)
            {
                JLogger.LogError($"[SaveSystem] 存档数据长度无效：{dataLength}，Key：{key}");
                return null;
            }

            var data = new byte[dataLength];
            Array.Copy(rawData, dataOffset, data, 0, dataLength);
            return data;
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("存档 Key 不能为空。", nameof(key));
        }

        private interface ISaveEntry
        {
            string Key { get; }
            SaveImportance Importance { get; }
            bool IsDirty { get; }
            UniTask RestoreAsync(CancellationToken ct);
            UniTask<SaveResult> SaveAsync(CancellationToken ct);
            void Attach();
            void Detach();
            bool Owns(StoreBase store);
        }

        private sealed class StoreSaveEntry<TData> : ISaveEntry where TData : class, new()
        {
            private readonly SaveSystem _owner;
            private readonly StoreBase<TData> _store;
            private long _revision;
            private long _savedRevision;
            private bool _attached;

            public StoreSaveEntry(
                SaveSystem owner,
                StoreBase<TData> store,
                string key,
                SaveImportance importance)
            {
                _owner = owner;
                _store = store;
                Key = key;
                Importance = importance;
            }

            public string Key { get; }
            public SaveImportance Importance { get; }
            public bool IsDirty => _revision != _savedRevision;

            public async UniTask RestoreAsync(CancellationToken ct)
            {
                var loaded = await _owner.LoadInternalAsync<TData>(Key, ct);
                if (loaded != null)
                    _store.ReplaceData(loaded);
            }

            public async UniTask<SaveResult> SaveAsync(CancellationToken ct)
            {
                var savingRevision = _revision;
                var result = await _owner.SaveInternalAsync(Key, _store.GetData(), ct);
                if (result.Success)
                    _savedRevision = savingRevision;
                return result;
            }

            public void Attach()
            {
                if (_attached) return;
                _store.DirtyMarked += OnDirty;
                _attached = true;
            }

            public void Detach()
            {
                if (!_attached) return;
                _store.DirtyMarked -= OnDirty;
                _attached = false;
            }

            public bool Owns(StoreBase store) => ReferenceEquals(_store, store);

            private void OnDirty()
            {
                _revision++;
                if (Importance == SaveImportance.Critical)
                    _owner.RequestCriticalSave(this);
            }
        }
    }
}
