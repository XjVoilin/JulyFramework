using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace July.Persistence
{
    public class KeyValueSaveSystem : SaveSystem
    {
        private readonly IKeyValueStore _store;
        private readonly string _keyPrefix;

        public KeyValueSaveSystem(IKeyValueStore store, string keyPrefix = "Save_")
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _keyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        }

        protected override UniTask<bool> WriteDataAsync(string key, byte[] data,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                _store.SetString(GetSavePath(key), Convert.ToBase64String(data));
                _store.Save();
                return UniTask.FromResult(true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return UniTask.FromResult(false);
            }
        }

        protected override UniTask<byte[]> ReadDataAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var storageKey = GetSavePath(key);
                if (!_store.HasKey(storageKey)) return UniTask.FromResult<byte[]>(null);
                var encoded = _store.GetString(storageKey);
                return UniTask.FromResult(string.IsNullOrEmpty(encoded)
                    ? null
                    : Convert.FromBase64String(encoded));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return UniTask.FromResult<byte[]>(null);
            }
        }

        protected override bool DataExists(string key) => _store.HasKey(GetSavePath(key));

        protected override bool DeleteData(string key)
        {
            var storageKey = GetSavePath(key);
            if (!_store.HasKey(storageKey)) return false;
            _store.DeleteKey(storageKey);
            _store.Save();
            return true;
        }

        public override string GetSavePath(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("存档 key 不能为空。", nameof(key));
            return _keyPrefix + key;
        }
    }

    public sealed class PlayerPrefsSaveSystem : KeyValueSaveSystem
    {
        public PlayerPrefsSaveSystem(string keyPrefix = "Save_")
            : base(new PlayerPrefsKeyValueStore(), keyPrefix) { }
    }
}
