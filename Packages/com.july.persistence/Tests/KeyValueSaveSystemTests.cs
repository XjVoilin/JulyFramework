using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using July.Arch;
using NUnit.Framework;

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

            public bool HasKey(string key) => _values.ContainsKey(key);
            public string GetString(string key) => _values.TryGetValue(key, out var value) ? value : null;
            public void SetString(string key, string value) => _values[key] = value;
            public void DeleteKey(string key) => _values.Remove(key);
            public void Save() { }
        }
    }
}
