using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Persistence;
using NUnit.Framework;

namespace July.Persistence.Tests.Save
{
    /// <summary>
    /// SaveSystem 单元测试。
    ///
    /// 用一个内存版子类 <see cref="InMemorySaveSystem"/> 实现 IO 抽象层，
    /// 配合一个纯字节 <see cref="BytesSerializeSystem"/>（不依赖 LitJson），
    /// 验证：注册/脏标记、SaveSignal 优先级调度、保存-加载往返、空 key 校验。
    /// 不触发真实的文件/PlayerPrefs IO，纯逻辑。
    /// </summary>
    [TestFixture]
    public class SaveSystemTests
    {
        private ArchContext _ctx;
        private InMemorySaveSystem _save;
        private BytesSerializeSystem _serializer;

        [SetUp]
        public void SetUp()
        {
            _ctx = new ArchContext();
            _save = new InMemorySaveSystem();
            _serializer = new BytesSerializeSystem();
            _ctx.RegisterSystem(_save);
            _ctx.RegisterSystem(_serializer);
            _ctx.InitializeAsync().GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _ctx?.Shutdown();
            _ctx = null;
        }

        #region 注册与脏标记

        [Test]
        public void Register_MakesKeyDirtyWhenMarked()
        {
            _save.Register("k", new SavePayload());

            Assert.IsFalse(_save.IsDirty("k"), "注册后默认非脏");
            Assert.IsTrue(_save.MarkDirty("k"), "已注册 key 可标记脏");
            Assert.IsTrue(_save.IsDirty("k"));
            Assert.AreEqual(1, _save.DirtyCount);
        }

        [Test]
        public void MarkDirty_UnregisteredKey_ReturnsFalse()
        {
            Assert.IsFalse(_save.MarkDirty("missing"), "未注册 key 标记脏应失败");
            Assert.AreEqual(0, _save.DirtyCount);
        }

        [Test]
        public void MarkDirty_EmptyKey_ReturnsFalse()
            => Assert.IsFalse(_save.MarkDirty(""));

        [Test]
        public void ClearAllDirty_ResetsDirtySet()
        {
            _save.Register("a", new SavePayload { Id = 1 });
            _save.Register("b", new SavePayload { Id = 2 });
            _save.MarkDirty("a");
            _save.MarkDirty("b");

            _save.ClearAllDirty();

            Assert.AreEqual(0, _save.DirtyCount);
        }

        [Test]
        public void Unregister_RemovesKey()
        {
            _save.Register("k", new SavePayload { Id = 1 });
            Assert.IsTrue(_save.IsRegistered("k"));

            Assert.IsTrue(_save.Unregister("k"));
            Assert.IsFalse(_save.IsRegistered("k"));
        }

        #endregion

        #region SaveSignal 优先级调度（ImportanceBasedSaveStrategy）

        [Test]
        public void TriggerSave_LowSignal_OnlySavesCritical()
        {
            var critical = new SavePayload { Id = 1, Importance = SaveImportance.Critical };
            var normal = new SavePayload { Id = 2, Importance = SaveImportance.Normal };
            _save.Register("crit", critical);
            _save.Register("norm", normal);
            _save.MarkDirty("crit");
            _save.MarkDirty("norm");

            var results = _save.TriggerSaveAsync(SaveSignal.Low).GetAwaiter().GetResult();

            Assert.IsTrue(results.ContainsKey("crit"), "Low 信号应保存 Critical");
            Assert.IsFalse(results.ContainsKey("norm"), "Low 信号不应保存 Normal");
        }

        [Test]
        public void TriggerSave_ImmediateSignal_SavesAll()
        {
            var critical = new SavePayload { Id = 1, Importance = SaveImportance.Critical };
            var trivial = new SavePayload { Id = 2, Importance = SaveImportance.Trivial };
            _save.Register("crit", critical);
            _save.Register("triv", trivial);
            _save.MarkDirty("crit");
            _save.MarkDirty("triv");

            var results = _save.TriggerSaveAsync(SaveSignal.Immediate).GetAwaiter().GetResult();

            Assert.IsTrue(results.ContainsKey("crit"));
            Assert.IsTrue(results.ContainsKey("triv"), "Immediate 信号应保存所有 importance");
        }

        [Test]
        public void TriggerSave_SuccessClearsDirty()
        {
            _save.Register("k", new SavePayload { Id = 1, Importance = SaveImportance.Critical });
            _save.MarkDirty("k");

            _save.TriggerSaveAsync(SaveSignal.Immediate).GetAwaiter().GetResult();

            Assert.IsFalse(_save.IsDirty("k"), "保存成功后应清除脏标记");
        }

        [Test]
        public void TriggerSave_NoDirtyKeys_ReturnsEmpty()
        {
            _save.Register("k", new SavePayload { Id = 1, Importance = SaveImportance.Critical });

            var results = _save.TriggerSaveAsync(SaveSignal.Immediate).GetAwaiter().GetResult();

            Assert.AreEqual(0, results.Count, "无脏 key 时不触发任何保存");
        }

        [Test]
        public void MarkDirtyAndSave_LowSignal_DoesNotSaveButMarksDirty()
        {
            // Low 信号只标记脏不实际保存（交给定时自动保存），返回 true。
            _save.Register("k", new SavePayload { Id = 1, Importance = SaveImportance.Critical });

            bool ok = _save.MarkDirtyAndSaveAsync("k", SaveSignal.Low).GetAwaiter().GetResult();

            Assert.IsTrue(ok);
            Assert.IsTrue(_save.IsDirty("k"), "Low 信号标记脏后应保持脏状态");
            Assert.AreEqual(0, _save.WriteCount, "Low 信号不应触发实际写盘");
        }

        [Test]
        public void MarkDirtyAndSave_HighSignal_SavesAndClearsDirty()
        {
            _save.Register("k", new SavePayload { Id = 1, Importance = SaveImportance.Normal });

            bool ok = _save.MarkDirtyAndSaveAsync("k", SaveSignal.High).GetAwaiter().GetResult();

            Assert.IsTrue(ok);
            Assert.IsFalse(_save.IsDirty("k"));
            Assert.AreEqual(1, _save.WriteCount, "High 信号应触发实际写盘");
        }

        #endregion

        #region 保存-加载往返

        [Test]
        public void SaveThenLoad_RoundTripsData()
        {
            var payload = new SavePayload { Id = 42, Importance = SaveImportance.Normal };

            _save.SaveAsync("slot", payload).GetAwaiter().GetResult();
            var loaded = _save.LoadAsync<SavePayload>("slot").GetAwaiter().GetResult();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(42, loaded.Id);
        }

        [Test]
        public void SaveAsync_EmptyKey_ReturnsInvalidData()
        {
            var result = _save.SaveAsync("", new SavePayload { Id = 1 }).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SaveFailureReason.InvalidData, result.FailureReason);
        }

        [Test]
        public void LoadAsync_MissingKey_ReturnsDefault()
        {
            var loaded = _save.LoadAsync<SavePayload>("never-saved").GetAwaiter().GetResult();

            Assert.IsNull(loaded, "不存在的 key 应返回 default");
        }

        [Test]
        public void HasSave_ReflectsExistence()
        {
            Assert.IsFalse(_save.HasSave("slot"));

            _save.SaveAsync("slot", new SavePayload { Id = 1 }).GetAwaiter().GetResult();

            Assert.IsTrue(_save.HasSave("slot"));
        }

        [Test]
        public void Delete_RemovesKeyAndData()
        {
            _save.SaveAsync("slot", new SavePayload { Id = 1 }).GetAwaiter().GetResult();
            Assert.IsTrue(_save.HasSave("slot"));

            bool deleted = _save.Delete("slot");

            Assert.IsTrue(deleted);
            Assert.IsFalse(_save.HasSave("slot"));
            Assert.IsFalse(_save.IsRegistered("slot"));
        }

        [Test]
        public void Save_OverwritesPreviousValue()
        {
            _save.SaveAsync("slot", new SavePayload { Id = 1 }).GetAwaiter().GetResult();
            _save.SaveAsync("slot", new SavePayload { Id = 99 }).GetAwaiter().GetResult();

            var loaded = _save.LoadAsync<SavePayload>("slot").GetAwaiter().GetResult();

            Assert.AreEqual(99, loaded.Id, "二次保存应覆盖旧值");
        }

        #endregion

        #region Stubs

        /// <summary>
        /// 内存版 SaveSystem：把序列化后的字节存在字典里，模拟读写盘。
        /// </summary>
        private sealed class InMemorySaveSystem : SaveSystem
        {
            private readonly Dictionary<string, byte[]> _store = new();
            public int WriteCount;

            protected override UniTask<bool> WriteDataAsync(string key, byte[] data, CancellationToken ct)
            {
                _store[key] = data;
                WriteCount++;
                return UniTask.FromResult(true);
            }

            protected override UniTask<byte[]> ReadDataAsync(string key, CancellationToken ct)
                => UniTask.FromResult(_store.TryGetValue(key, out var data) ? data : null);

            protected override bool DataExists(string key) => _store.ContainsKey(key);

            protected override bool DeleteData(string key) => _store.Remove(key);

            public override string GetSavePath(string key) => key;
        }

        /// <summary>
        /// 极简字节序列化器：把对象装箱为 byte[] 再还原。
        /// 不依赖 LitJson，仅用于测试 SaveSystem 的管线编排，不验证真实序列化正确性。
        /// </summary>
        private sealed class BytesSerializeSystem : SystemBase, ISerializeSystem
        {
            // 用一个简单的"装箱+类型标记"方案，保证 Save/Load 往返自洽。
            private readonly Dictionary<int, object> _registry = new();
            private int _counter;

            public byte[] Serialize<T>(T data)
            {
                if (data == null) return Array.Empty<byte>();
                var id = ++_counter;
                _registry[id] = data;
                return BitConverter.GetBytes(id);
            }

            public T Deserialize<T>(byte[] bytes)
            {
                if (bytes == null || bytes.Length == 0) return default;
                var id = BitConverter.ToInt32(bytes, 0);
                return _registry.TryGetValue(id, out var obj) ? (T)obj : default;
            }

            public string SerializeToJson(object data) => throw new NotImplementedException();
            public object DeserializeFromJson(string json, Type type) => throw new NotImplementedException();
        }

        /// <summary>测试用可保存数据：带 Id 与 Importance。</summary>
        private sealed class SavePayload : ISaveData
        {
            public int Id;
            public SaveImportance Importance { get; set; } = SaveImportance.Normal;
        }

        #endregion
    }
}
