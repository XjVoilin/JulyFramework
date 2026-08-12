using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using NUnit.Framework;

namespace July.Persistence.Tests.Save
{
    [TestFixture]
    public class SaveSystemTests
    {
        private ArchContext _context;
        private InMemorySaveSystem _saveSystem;
        private BytesSerializeSystem _serializeSystem;

        [SetUp]
        public void SetUp()
        {
            _context = new ArchContext();
            _saveSystem = new InMemorySaveSystem();
            _serializeSystem = new BytesSerializeSystem();
            RegisterPersistenceSystems();
        }

        [TearDown]
        public void TearDown()
        {
            _context?.Shutdown();
            _context = null;
        }

        [Test]
        public void Persist_ReturnsSameStoreAndRestoresDefaultWhenSaveMissing()
        {
            var store = new TestStore();

            var returned = _saveSystem.Persist(store, "settings", SaveImportance.Important);
            _context.RegisterStore(store);
            _context.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreSame(store, returned);
            Assert.AreEqual(0, store.Id);
            Assert.AreEqual(0, FlushAll().Count);
        }

        [Test]
        public void Persist_RestoresSavedDataBeforeLaterSystemInitializes()
        {
            var source = new TestStore();
            _saveSystem.Persist(source, "account", SaveImportance.Important);
            _context.RegisterStore(source);
            _context.InitializeAsync().GetAwaiter().GetResult();
            source.SetId(42);
            _saveSystem.SaveNowAsync(source).GetAwaiter().GetResult();

            RestartContext();

            var restored = new TestStore();
            var dependant = new StoreReadingSystem();
            _saveSystem.Persist(restored, "account", SaveImportance.Important);
            _context.RegisterStore(restored);
            _context.RegisterSystem(dependant);
            _context.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(42, restored.Id);
            Assert.AreEqual(42, dependant.InitialId);
        }

        [Test]
        public void StoreMutation_OnlyFlushesDeclaredStore()
        {
            var persistentStore = new TestStore();
            var transientStore = new OtherTestStore();
            _saveSystem.Persist(persistentStore, "persistent", SaveImportance.Normal);
            _context.RegisterStore(persistentStore);
            _context.RegisterStore(transientStore);
            _context.InitializeAsync().GetAwaiter().GetResult();

            persistentStore.SetId(1);
            transientStore.SetId(2);
            var results = FlushAll();

            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results.ContainsKey("persistent"));
        }

        [Test]
        public void ReplaceData_MarksDeclaredStoreForFlush()
        {
            var store = new TestStore();
            _saveSystem.Persist(store, "store", SaveImportance.Normal);
            _context.RegisterStore(store);
            _context.InitializeAsync().GetAwaiter().GetResult();

            store.ReplaceData(new SavePayload { Id = 7 });
            var results = FlushAll();

            Assert.IsTrue(results.ContainsKey("store"));
        }

        [Test]
        public void Flush_UsesDeclaredImportance()
        {
            var critical = new TestStore();
            var normal = new OtherTestStore();
            _saveSystem.Persist(critical, "critical", SaveImportance.Critical);
            _saveSystem.Persist(normal, "normal", SaveImportance.Normal);
            _context.RegisterStore(critical);
            _context.RegisterStore(normal);
            _context.InitializeAsync().GetAwaiter().GetResult();
            _saveSystem.FailNextWrite();
            critical.SetId(1);
            normal.SetId(2);

            var lowResults = _saveSystem.FlushAsync(SaveSignal.Low)
                .GetAwaiter().GetResult();
            var highResults = _saveSystem.FlushAsync(SaveSignal.High)
                .GetAwaiter().GetResult();

            Assert.IsTrue(lowResults.ContainsKey("critical"));
            Assert.IsFalse(lowResults.ContainsKey("normal"));
            Assert.IsTrue(highResults.ContainsKey("normal"));
        }

        [Test]
        public void CriticalStore_MutationSavesWithoutExplicitFlush()
        {
            var store = new TestStore();
            _saveSystem.Persist(store, "critical", SaveImportance.Critical);
            _context.RegisterStore(store);
            _context.InitializeAsync().GetAwaiter().GetResult();

            store.SetId(7);

            RestartContext();
            var restored = new TestStore();
            _saveSystem.Persist(restored, "critical", SaveImportance.Critical);
            _context.RegisterStore(restored);
            _context.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(7, restored.Id);
        }

        [Test]
        public void NonCriticalStore_MutationWaitsForFlush()
        {
            var store = new TestStore();
            _saveSystem.Persist(store, "normal", SaveImportance.Normal);
            _context.RegisterStore(store);
            _context.InitializeAsync().GetAwaiter().GetResult();

            store.SetId(7);

            RestartContext();
            var restored = new TestStore();
            _saveSystem.Persist(restored, "normal", SaveImportance.Normal);
            _context.RegisterStore(restored);
            _context.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(0, restored.Id);
        }

        [Test]
        public void FailedCriticalSave_RemainsDirtyForRetry()
        {
            var store = new TestStore();
            _saveSystem.Persist(store, "critical", SaveImportance.Critical);
            _context.RegisterStore(store);
            _context.InitializeAsync().GetAwaiter().GetResult();
            _saveSystem.FailNextWrite();

            store.SetId(9);
            var retryResults = FlushAll();

            Assert.IsTrue(retryResults.TryGetValue("critical", out var result));
            Assert.IsTrue(result.Success);
        }

        [Test]
        public void SaveNow_SavesCurrentStoreAndAcceptsStoreInsteadOfKey()
        {
            var store = new TestStore();
            _saveSystem.Persist(store, "store", SaveImportance.Trivial);
            _context.RegisterStore(store);
            _context.InitializeAsync().GetAwaiter().GetResult();
            store.SetId(99);

            var result = _saveSystem.SaveNowAsync(store).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, FlushAll().Count);

            RestartContext();
            var restored = new TestStore();
            _saveSystem.Persist(restored, "store", SaveImportance.Trivial);
            _context.RegisterStore(restored);
            _context.InitializeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(99, restored.Id);
        }

        [Test]
        public void SaveNow_UndeclaredStoreReturnsFailure()
        {
            _context.InitializeAsync().GetAwaiter().GetResult();

            var result = _saveSystem.SaveNowAsync(new TestStore())
                .GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SaveFailureReason.InvalidData, result.FailureReason);
        }

        [Test]
        public void Persist_DuplicateKeyThrows()
        {
            _saveSystem.Persist(new TestStore(), "same", SaveImportance.Normal);

            Assert.Throws<InvalidOperationException>(() =>
                _saveSystem.Persist(new OtherTestStore(), "same", SaveImportance.Normal));
        }

        [Test]
        public void Shutdown_DetachesStoreDirtySignalAndClearsDeclarations()
        {
            var store = new TestStore();
            _saveSystem.Persist(store, "store", SaveImportance.Normal);
            _context.RegisterStore(store);
            _context.InitializeAsync().GetAwaiter().GetResult();

            _context.Shutdown();
            store.SetId(1);

            Assert.AreEqual(0, FlushAll().Count);
        }

        [Test]
        public void MutationDuringWrite_RemainsPendingAfterCompletedSave()
        {
            var store = new TestStore();
            _saveSystem.Persist(store, "store", SaveImportance.Normal);
            _context.RegisterStore(store);
            _context.InitializeAsync().GetAwaiter().GetResult();
            store.SetId(1);
            _saveSystem.PauseNextWrite();

            var firstSave = _saveSystem.SaveNowAsync(store);
            store.SetId(2);
            _saveSystem.ResumeWrite();
            firstSave.GetAwaiter().GetResult();

            var pendingResults = FlushAll();
            Assert.IsTrue(pendingResults.ContainsKey("store"));

            RestartContext();
            var restored = new TestStore();
            _saveSystem.Persist(restored, "store", SaveImportance.Normal);
            _context.RegisterStore(restored);
            _context.InitializeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(2, restored.Id);
        }

        [Test]
        public void ConcurrentSaveRequests_AreSerialized()
        {
            var store = new TestStore();
            _saveSystem.Persist(store, "store", SaveImportance.Normal);
            _context.RegisterStore(store);
            _context.InitializeAsync().GetAwaiter().GetResult();
            store.SetId(1);
            _saveSystem.PauseNextWrite();

            var firstSave = _saveSystem.SaveNowAsync(store);
            store.SetId(2);
            var secondSave = _saveSystem.SaveNowAsync(store);

            Assert.AreEqual(1, _saveSystem.ActiveWrites);
            _saveSystem.ResumeWrite();
            firstSave.GetAwaiter().GetResult();
            secondSave.GetAwaiter().GetResult();

            Assert.AreEqual(1, _saveSystem.MaxConcurrentWrites);
        }

        [Test]
        public void CriticalStore_ContinuousMutationsRemainSerializedAndSaveLatestData()
        {
            var store = new TestStore();
            _saveSystem.Persist(store, "critical", SaveImportance.Critical);
            _context.RegisterStore(store);
            _context.InitializeAsync().GetAwaiter().GetResult();
            _saveSystem.PauseNextWrite();

            store.SetId(1);
            store.SetId(2);
            store.SetId(3);

            Assert.AreEqual(1, _saveSystem.ActiveWrites);
            _saveSystem.ResumeWrite();
            FlushAll();
            Assert.AreEqual(1, _saveSystem.MaxConcurrentWrites);

            RestartContext();
            var restored = new TestStore();
            _saveSystem.Persist(restored, "critical", SaveImportance.Critical);
            _context.RegisterStore(restored);
            _context.InitializeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(3, restored.Id);
        }

        private IReadOnlyDictionary<string, SaveResult> FlushAll()
            => _saveSystem.FlushAsync(SaveSignal.Immediate).GetAwaiter().GetResult();

        private void RegisterPersistenceSystems()
        {
            _context.RegisterSystem(_serializeSystem);
            _context.RegisterSystem(_saveSystem);
        }

        private void RestartContext()
        {
            _context.Shutdown();
            _context = new ArchContext();
            RegisterPersistenceSystems();
        }

        private sealed class TestStore : StoreBase<SavePayload>
        {
            public int Id => Data.Id;

            public void SetId(int id)
            {
                Data.Id = id;
                MarkDirty();
            }
        }

        private sealed class OtherTestStore : StoreBase<SavePayload>
        {
            public void SetId(int id)
            {
                Data.Id = id;
                MarkDirty();
            }
        }

        private sealed class StoreReadingSystem : SystemBase
        {
            public int InitialId { get; private set; }

            protected override UniTask OnInitializeAsync()
            {
                InitialId = GetStore<TestStore>().Id;
                return UniTask.CompletedTask;
            }
        }

        private sealed class SavePayload
        {
            public int Id;
        }

        private sealed class InMemorySaveSystem : SaveSystem
        {
            private readonly Dictionary<string, byte[]> _storage = new();
            private UniTaskCompletionSource _nextWriteGate;
            private UniTaskCompletionSource _activeWriteGate;
            private bool _failNextWrite;

            public int ActiveWrites { get; private set; }
            public int MaxConcurrentWrites { get; private set; }

            public void PauseNextWrite() => _nextWriteGate = new UniTaskCompletionSource();

            public void FailNextWrite() => _failNextWrite = true;

            public void ResumeWrite()
            {
                (_activeWriteGate ?? _nextWriteGate)?.TrySetResult();
            }

            protected override async UniTask<bool> WriteDataAsync(
                string key,
                byte[] data,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                ActiveWrites++;
                if (ActiveWrites > MaxConcurrentWrites)
                    MaxConcurrentWrites = ActiveWrites;

                try
                {
                    _activeWriteGate = _nextWriteGate;
                    _nextWriteGate = null;
                    if (_activeWriteGate != null)
                        await _activeWriteGate.Task;

                    if (_failNextWrite)
                    {
                        _failNextWrite = false;
                        return false;
                    }

                    _storage[key] = data;
                    return true;
                }
                finally
                {
                    _activeWriteGate = null;
                    ActiveWrites--;
                }
            }

            protected override UniTask<byte[]> ReadDataAsync(string key, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.FromResult(_storage.TryGetValue(key, out var data) ? data : null);
            }

            protected override bool DataExists(string key) => _storage.ContainsKey(key);
        }

        private sealed class BytesSerializeSystem : SystemBase, ISerializeSystem
        {
            private readonly Dictionary<int, object> _registry = new();
            private int _counter;

            public byte[] Serialize<T>(T data)
            {
                if (data == null) return Array.Empty<byte>();
                var id = ++_counter;
                _registry[id] = data is SavePayload payload
                    ? new SavePayload { Id = payload.Id }
                    : (object)data;
                return BitConverter.GetBytes(id);
            }

            public T Deserialize<T>(byte[] bytes)
            {
                if (bytes == null || bytes.Length == 0) return default;
                var id = BitConverter.ToInt32(bytes, 0);
                return _registry.TryGetValue(id, out var value) ? (T)value : default;
            }

            public string SerializeToJson(object data) => throw new NotImplementedException();
            public object DeserializeFromJson(string json, Type type) => throw new NotImplementedException();
        }
    }
}
