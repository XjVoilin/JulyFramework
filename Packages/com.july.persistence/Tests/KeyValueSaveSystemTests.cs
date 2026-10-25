using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.Persistence.Tests
{
    public class KeyValueSaveSystemTests
    {
        [Test]
        public void PersistedStore_UsesInjectedKeyPrefix()
        {
            var memory = new MemoryStore();
            var context = CreateContext(memory, out var saveSystem);
            var store = new TestStore();
            saveSystem.Persist(store, "user", SaveImportance.Normal);
            context.RegisterStore(store);

            try
            {
                context.InitializeAsync().GetAwaiter().GetResult();
                store.SetId(7);
                var result = saveSystem.SaveNowAsync(store).GetAwaiter().GetResult();

                Assert.IsTrue(result.Success);
                Assert.IsTrue(memory.HasKey("profile/user"));
            }
            finally
            {
                context.Shutdown();
            }
        }

        [Test]
        public void PersistedStore_RestoresThroughSameKeyPrefix()
        {
            var memory = new MemoryStore();
            var firstContext = CreateContext(memory, out var firstSaveSystem);
            var source = new TestStore();
            firstSaveSystem.Persist(source, "user", SaveImportance.Normal);
            firstContext.RegisterStore(source);
            firstContext.InitializeAsync().GetAwaiter().GetResult();
            source.SetId(11);
            firstSaveSystem.SaveNowAsync(source).GetAwaiter().GetResult();
            firstContext.Shutdown();

            var secondContext = CreateContext(memory, out var secondSaveSystem);
            var restored = new TestStore();
            secondSaveSystem.Persist(restored, "user", SaveImportance.Normal);
            secondContext.RegisterStore(restored);

            try
            {
                secondContext.InitializeAsync().GetAwaiter().GetResult();
                Assert.AreEqual(11, restored.Id);
            }
            finally
            {
                secondContext.Shutdown();
            }
        }

        [TestCase("{\"Importance\":0,\"Entries\":[]}")]
        [TestCase("not-base64!")]
        [TestCase("AAAA===")]
        public void InvalidBase64_ResetsOnlyInvalidStoreAndFlushesDeletion(string encoded)
        {
            var memory = new MemoryStore();
            var firstContext = CreateContext(memory, out var firstSaveSystem);
            var source = new TestStore();
            firstSaveSystem.Persist(source, "valid", SaveImportance.Normal);
            firstContext.RegisterStore(source);
            try
            {
                firstContext.InitializeAsync().GetAwaiter().GetResult();
                source.SetId(23);
                Assert.IsTrue(firstSaveSystem.SaveNowAsync(source).GetAwaiter().GetResult().Success);
            }
            finally
            {
                firstContext.Shutdown();
            }

            var validEncoded = memory.GetString("profile/valid");
            memory.SetString("profile/invalid", encoded);
            var savesBeforeRestore = memory.SaveCount;
            var context = CreateContext(memory, out var saveSystem);
            var invalid = new TestStore();
            var valid = new TestStore();
            saveSystem.Persist(invalid, "invalid", SaveImportance.Normal);
            saveSystem.Persist(valid, "valid", SaveImportance.Normal);
            context.RegisterStore(invalid);
            context.RegisterStore(valid);
#if JULYGF_DEBUG
            LogAssert.Expect(LogType.Warning,
                "[SaveSystem] 已清理无效的 Base64 存档：profile/invalid，将使用默认数据。");
#endif

            try
            {
                context.InitializeAsync().GetAwaiter().GetResult();

                Assert.AreEqual(0, invalid.Id);
                Assert.AreEqual(23, valid.Id);
                Assert.IsFalse(memory.HasKey("profile/invalid"));
                Assert.AreEqual(validEncoded, memory.GetString("profile/valid"));
                Assert.AreEqual(savesBeforeRestore + 1, memory.SaveCount);
                Assert.AreEqual(1, memory.DeleteCount);
            }
            finally
            {
                context.Shutdown();
            }
        }

        [Test]
        public void InvalidBase64_CanSaveAndRestoreAfterReset()
        {
            var memory = new MemoryStore();
            memory.SetString("profile/user", "{\"Id\":7}");
            var firstContext = CreateContext(memory, out var firstSaveSystem);
            var source = new TestStore();
            firstSaveSystem.Persist(source, "user", SaveImportance.Normal);
            firstContext.RegisterStore(source);
#if JULYGF_DEBUG
            LogAssert.Expect(LogType.Warning,
                "[SaveSystem] 已清理无效的 Base64 存档：profile/user，将使用默认数据。");
#endif

            try
            {
                firstContext.InitializeAsync().GetAwaiter().GetResult();
                source.SetId(42);
                Assert.IsTrue(firstSaveSystem.SaveNowAsync(source).GetAwaiter().GetResult().Success);
            }
            finally
            {
                firstContext.Shutdown();
            }

            var secondContext = CreateContext(memory, out var secondSaveSystem);
            var restored = new TestStore();
            secondSaveSystem.Persist(restored, "user", SaveImportance.Normal);
            secondContext.RegisterStore(restored);
            try
            {
                secondContext.InitializeAsync().GetAwaiter().GetResult();
                Assert.AreEqual(42, restored.Id);
                Assert.AreEqual(1, memory.DeleteCount);
            }
            finally
            {
                secondContext.Shutdown();
            }
        }

        [Test]
        public void ReadFailure_EvenFormatExceptionDoesNotDeleteStoredValue()
        {
            var memory = new MemoryStore();
            memory.SetString("profile/user", "{\"Id\":7}");
            memory.ReadException = new FormatException("Storage read failed");
            var saveSystem = new ReadableKeyValueSaveSystem(memory);
            LogAssert.Expect(LogType.Exception, new Regex("FormatException: Storage read failed"));

            Assert.IsNull(saveSystem.ReadAsync("user").GetAwaiter().GetResult());
            Assert.IsTrue(memory.HasKey("profile/user"));
            Assert.AreEqual(0, memory.DeleteCount);
            Assert.AreEqual(0, memory.SaveCount);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InvalidBase64_CleanupFailureIsReported(bool failDelete)
        {
            var memory = new MemoryStore();
            memory.SetString("profile/user", "{\"Id\":7}");
            if (failDelete)
                memory.DeleteException = new InvalidOperationException("Storage cleanup failed");
            else
                memory.SaveException = new InvalidOperationException("Storage cleanup failed");
            var saveSystem = new ReadableKeyValueSaveSystem(memory);
            LogAssert.Expect(LogType.Exception,
                new Regex("InvalidOperationException: Storage cleanup failed"));

            Assert.IsNull(saveSystem.ReadAsync("user").GetAwaiter().GetResult());
            Assert.AreEqual(failDelete, memory.HasKey("profile/user"));
            Assert.AreEqual(1, memory.DeleteCount);
            Assert.AreEqual(failDelete ? 0 : 1, memory.SaveCount);
            LogAssert.NoUnexpectedReceived();
        }

        private sealed class ReadableKeyValueSaveSystem : KeyValueSaveSystem
        {
            public ReadableKeyValueSaveSystem(IKeyValueStore store) : base(store, "profile/") { }

            public UniTask<byte[]> ReadAsync(string key)
                => ReadDataAsync(key, CancellationToken.None);
        }

        private static ArchContext CreateContext(
            MemoryStore memory,
            out KeyValueSaveSystem saveSystem)
        {
            var context = new ArchContext();
            saveSystem = new KeyValueSaveSystem(memory, "profile/");
            context.RegisterSystem(new PayloadSerializeSystem());
            context.RegisterSystem(saveSystem);
            return context;
        }

        private sealed class TestStore : StoreBase<Payload>
        {
            public int Id => Data.Id;

            public void SetId(int id)
            {
                Data.Id = id;
                MarkDirty();
            }
        }

        private sealed class Payload
        {
            public int Id;
        }

        private sealed class PayloadSerializeSystem : SystemBase, ISerializeSystem
        {
            public byte[] Serialize<T>(T data)
                => data is Payload payload ? BitConverter.GetBytes(payload.Id) : Array.Empty<byte>();

            public T Deserialize<T>(byte[] bytes)
                => (T)(object)new Payload { Id = BitConverter.ToInt32(bytes, 0) };

            public string SerializeToJson(object data) => throw new NotImplementedException();
            public object DeserializeFromJson(string json, Type type) => throw new NotImplementedException();
        }

        private sealed class MemoryStore : IKeyValueStore
        {
            private readonly Dictionary<string, string> _values = new();

            public int DeleteCount { get; private set; }
            public int SaveCount { get; private set; }
            public Exception ReadException { get; set; }
            public Exception DeleteException { get; set; }
            public Exception SaveException { get; set; }

            public bool HasKey(string key) => _values.ContainsKey(key);

            public string GetString(string key)
            {
                if (ReadException != null) throw ReadException;
                return _values.TryGetValue(key, out var value) ? value : null;
            }

            public void SetString(string key, string value) => _values[key] = value;

            public void DeleteKey(string key)
            {
                DeleteCount++;
                if (DeleteException != null) throw DeleteException;
                _values.Remove(key);
            }

            public void Save()
            {
                SaveCount++;
                if (SaveException != null) throw SaveException;
            }
        }
    }
}
