using System;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.Arch.Tests
{
    /// <summary>
    /// ArchContext 注册 / 初始化 / Shutdown 顺序 / 增量初始化 / 事件清理 的单元测试。
    /// 纯逻辑验证框架骨架，不依赖场景与资源。
    /// </summary>
    [TestFixture]
    public class ArchContextTests
    {
        private static int s_clock;
        private ArchContext _ctx;

        [SetUp]
        public void SetUp()
        {
            s_clock = 0;
            _ctx = new ArchContext();
        }

        [TearDown]
        public void TearDown()
        {
            _ctx?.Shutdown();
            _ctx = null;
        }

        #region 查询：未注册返回 null

        [Test]
        public void GetStore_NotRegistered_ReturnsNull()
        {
            LogAssert.Expect(LogType.Error, new Regex("GetStore.*未注册"));
            Assert.IsNull(_ctx.GetStore<TestStore>());
        }

        [Test]
        public void GetSystem_NotRegistered_ReturnsNull()
        {
            LogAssert.Expect(LogType.Error, new Regex("GetSystem.*未注册"));
            Assert.IsNull(_ctx.GetSystem<TestSystem>());
        }

        #endregion

        #region 注册 + 初始化顺序

        [Test]
        public void InitializeAsync_StoreBeforeSystem()
        {
            var store = new TestStore();
            var system = new TestSystem();
            _ctx.RegisterStore(store);
            _ctx.RegisterSystem(system);

            _ctx.InitializeAsync().GetAwaiter().GetResult();

            Assert.IsTrue(store.InitCalled, "Store.OnInitializeAsync 应在初始化时被调用");
            Assert.IsTrue(system.InitCalled, "System.OnInitializeAsync 应在初始化时被调用");
            Assert.IsTrue(system.PostInitCalled, "System.OnPostInitialize 应在初始化时被调用");
            Assert.Less(store.InitTick, system.InitTick,
                "Store 应在 System 之前初始化");
            Assert.Less(system.InitTick, system.PostInitTick,
                "PostInitialize 必须晚于 InitializeAsync");
            Assert.IsTrue(_ctx.Initialized);
        }

        [Test]
        public void InitializeAsync_Idempotent_SkipsAlreadyInitialized()
        {
            var store = new TestStore();
            _ctx.RegisterStore(store);
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            var firstTick = store.InitTick;
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(firstTick, store.InitTick, "幂等：重复初始化不应再次调用 OnInitializeAsync");
            Assert.IsTrue(_ctx.Initialized);
        }

        [Test]
        public void InitializeAsync_Incremental_InitializesNewlyRegistered()
        {
            var sys1 = new TestSystem();
            _ctx.RegisterSystem(sys1);
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            Assert.IsTrue(sys1.InitCalled);
            var sys1Tick = sys1.InitTick;

            var sys2 = new TestSystem2();
            _ctx.RegisterSystem(sys2);
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(sys1Tick, sys1.InitTick, "已初始化的 System 不应重复初始化");
            Assert.IsTrue(sys2.InitCalled, "新注册的 System 应在第二次 InitializeAsync 时初始化");
        }

        [Test]
        public void InitializeAsync_PostInitialize_AllSystemsReady()
        {
            var sysA = new PostInitCheckSystem();
            var sysB = new TestSystem();
            _ctx.RegisterSystem(sysA);
            _ctx.RegisterSystem(sysB);

            _ctx.InitializeAsync().GetAwaiter().GetResult();

            Assert.IsTrue(sysA.FoundSysBInPostInit,
                "OnPostInitialize 时应能访问后注册的 System");
        }

        [Test]
        public void RegisterSystem_AfterInitialize_DoesNotAutoInit()
        {
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            var system = new TestSystem();
            _ctx.RegisterSystem(system);

            Assert.IsFalse(system.InitCalled, "Register 只注册，不自动初始化");
        }

        [Test]
        public void RegisterStore_AfterInitialize_DoesNotAutoInit()
        {
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            var store = new TestStore();
            _ctx.RegisterStore(store);

            Assert.IsFalse(store.InitCalled, "Register 只注册，不自动初始化");
        }

        [Test]
        public void RunProcedure_ThrowsBeforeInitialize()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _ctx.RunProcedure(new NoopProcedure()).GetAwaiter().GetResult());
        }

        [Test]
        public void RunProcedure_NullArg_Throws()
        {
            _ctx.InitializeAsync().GetAwaiter().GetResult();
            Assert.Throws<ArgumentNullException>(() =>
                _ctx.RunProcedure(null).GetAwaiter().GetResult());
        }

        #endregion

        #region System 多接口键查询

        [Test]
        public void GetSystem_ByInterface_ReturnsImplementation()
        {
            var system = new TestSystem();
            _ctx.RegisterSystem(system);
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreSame(system, _ctx.GetSystem<ITickSystem>());
            Assert.AreSame(system, _ctx.GetSystem<TestSystemBase>());
        }

        [Test]
        public void RegisterSystem_DuplicateType_SkipsSecond()
        {
            var a = new TestSystem();
            var b = new TestSystem();
            _ctx.RegisterSystem(a);
            _ctx.RegisterSystem(b);

            Assert.AreSame(a, _ctx.GetSystem<TestSystem>(), "重复注册应保留先注册者");
        }

        #endregion

        #region Shutdown 顺序 + System 事件兜底注销

        [Test]
        public void Shutdown_SystemsBeforeStores()
        {
            var store = new TestStore();
            var system = new TestSystem();
            _ctx.RegisterStore(store);
            _ctx.RegisterSystem(system);
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            _ctx.Shutdown();

            Assert.GreaterOrEqual(system.ShutdownTick, 0);
            Assert.GreaterOrEqual(store.ShutdownTick, 0);
            Assert.Less(system.ShutdownTick, store.ShutdownTick,
                "System 应在 Store 之前 Shutdown（逆序注销）");
        }

        [Test]
        public void Shutdown_WithoutInitialize_DoesNotThrow()
        {
            var system = new TestSystem();
            _ctx.RegisterSystem(system);

            Assert.DoesNotThrow(() => _ctx.Shutdown());
            Assert.IsFalse(system.ShutdownCalled,
                "未初始化的 System 不应触发 OnShutdown");
        }

        [Test]
        public void Shutdown_SystemBaseAutoUnsubscribesEvents()
        {
            var system = new TestSubscribingSystem();
            _ctx.RegisterSystem(system);
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            _ctx.Event.Publish(new TestEvent());
            Assert.AreEqual(1, system.ReceivedCount, "初始化后事件应路由到 System");

            _ctx.UnregisterSystem(system);

            _ctx.Event.Publish(new TestEvent());
            Assert.AreEqual(1, system.ReceivedCount,
                "Shutdown 兜底 UnsubscribeAll 后，System 不应再收到事件");
        }

        [Test]
        public void UnregisterSystem_BeforeInit_DoesNotShutdown()
        {
            var system = new TestSystem();
            _ctx.RegisterSystem(system);

            _ctx.UnregisterSystem(system);

            Assert.IsFalse(system.ShutdownCalled,
                "未初始化的 System 注销时不应触发 OnShutdown");
            LogAssert.Expect(LogType.Error, new Regex("GetSystem.*未注册"));
            Assert.IsNull(_ctx.GetSystem<TestSystem>());
        }

        [Test]
        public void RunProcedure_AutoUnsubscribesAfterExecute()
        {
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            var proc = new SubscribingProcedure();
            int received = 0;
            _ctx.Event.Subscribe<TestEvent>(e => received++, this);

            _ctx.RunProcedure(proc).GetAwaiter().GetResult();
            Assert.AreEqual(1, proc.ReceivedCount, "Procedure 执行期间应收到事件");

            _ctx.Event.Publish(new TestEvent());
            Assert.AreEqual(1, proc.ReceivedCount, "Procedure 结束后不应再收到事件");
            Assert.AreEqual(2, received, "外部订阅者不受影响");
        }

        [Test]
        public void RunProcedure_AutoUnsubscribesEvenOnException()
        {
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            var proc = new ThrowingProcedure();
            Assert.Throws<InvalidOperationException>(() =>
                _ctx.RunProcedure(proc).GetAwaiter().GetResult());

            _ctx.Event.Publish(new TestEvent());
            Assert.AreEqual(0, proc.ReceivedCount, "异常退出后 Procedure 不应再收到事件");
        }

        [Test]
        public void Shutdown_ReleasesCurrent()
        {
            Assert.AreSame(_ctx, ArchContext.Current);
            _ctx.Shutdown();
            Assert.IsNull(ArchContext.Current, "Shutdown 后 Current 应被清空");
        }

        [Test]
        public void Shutdown_Twice_IsNoop()
        {
            _ctx.InitializeAsync().GetAwaiter().GetResult();
            _ctx.Shutdown();
            Assert.DoesNotThrow(() => _ctx.Shutdown(), "重复 Shutdown 不应抛异常");
        }

        #endregion

        #region View 注册与身份校验

        [Test]
        public void GetView_NotRegistered_ReturnsNull()
        {
            LogAssert.Expect(LogType.Error, new Regex("GetView.*未注册"));
            Assert.IsNull(_ctx.GetView<TestSingletonView>());
        }

        [Test]
        public void RegisterView_UnregisterView_IdentitySafe()
        {
            var go1 = new GameObject("v1");
            var view1 = go1.AddComponent<TestSingletonView>();

            var go2 = new GameObject("v2");
            go2.SetActive(false);
            var view2 = go2.AddComponent<TestSingletonView>();
            try
            {
                Assert.AreSame(view1, _ctx.GetView<TestSingletonView>());

                _ctx.UnregisterView(view2);
                Assert.AreSame(view1, _ctx.GetView<TestSingletonView>(),
                    "仅当登记的确实是自己时才移除");

                _ctx.UnregisterView(view1);
                LogAssert.Expect(LogType.Error, new Regex("GetView.*未注册"));
                Assert.IsNull(_ctx.GetView<TestSingletonView>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go1);
                UnityEngine.Object.DestroyImmediate(go2);
            }
        }

        #endregion

        #region Store 异步初始化

        [Test]
        public void InitializeAsync_AsyncStore_InitializedViaOnInitializeAsync()
        {
            var store = new TestAsyncStore();
            _ctx.RegisterStore(store);
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            Assert.IsTrue(store.InitCalled, "Store 应通过 OnInitializeAsync 初始化");
        }

        #endregion

        #region Update 驱动

        [Test]
        public void Update_DrivesUpdatableSystems()
        {
            var system = new TestSystem();
            _ctx.RegisterSystem(system);
            _ctx.InitializeAsync().GetAwaiter().GetResult();

            _ctx.Update(0.1f);
            _ctx.Update(0.2f);

            Assert.AreEqual(2, system.UpdateCount, "IUpdatableSystem 应被 Update 驱动");
            Assert.AreEqual(0.3f, system.TotalDelta, 0.0001f, "deltaTime 应被透传累加");
        }

        [Test]
        public void Update_BeforeInitialize_IsNoop()
        {
            var system = new TestSystem();
            _ctx.RegisterSystem(system);

            Assert.DoesNotThrow(() => _ctx.Update(0.1f));
            Assert.AreEqual(0, system.UpdateCount, "未初始化时 Update 不应驱动 System");
        }

        #endregion

        #region Stubs

        private struct TestEvent { }

        private abstract class TestSystemBase : SystemBase { }

        private interface ITickSystem { void Tick(); }

        private sealed class TestSystem : TestSystemBase, IUpdatableSystem, ITickSystem
        {
            public bool InitCalled, PostInitCalled, ShutdownCalled;
            public int InitTick = -1, PostInitTick = -1, ShutdownTick = -1;
            public int UpdateCount;
            public float TotalDelta;

            public void Tick() { }

            protected override UniTask OnInitializeAsync()
            {
                InitCalled = true;
                InitTick = s_clock++;
                return UniTask.CompletedTask;
            }

            protected override void OnPostInitialize()
            {
                PostInitCalled = true;
                PostInitTick = s_clock++;
            }

            protected override void OnShutdown()
            {
                ShutdownCalled = true;
                ShutdownTick = s_clock++;
            }

            public void OnUpdate(float deltaTime)
            {
                UpdateCount++;
                TotalDelta += deltaTime;
            }
        }

        private sealed class PostInitCheckSystem : SystemBase
        {
            public bool FoundSysBInPostInit;

            protected override void OnPostInitialize()
            {
                FoundSysBInPostInit = TryGetSystem<TestSystem>() != null;
            }
        }

        private sealed class TestSystem2 : SystemBase
        {
            public bool InitCalled;
            public int InitTick = -1;

            protected override UniTask OnInitializeAsync()
            {
                InitCalled = true;
                InitTick = s_clock++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class TestSubscribingSystem : SystemBase
        {
            public int ReceivedCount;

            protected override UniTask OnInitializeAsync()
            {
                Subscribe<TestEvent>(OnEvent);
                return UniTask.CompletedTask;
            }

            private void OnEvent(TestEvent e) => ReceivedCount++;
        }

        private sealed class TestStoreData { }

        private class TestStore : StoreBase<TestStoreData>
        {
            public bool InitCalled, ShutdownCalled;
            public int InitTick = -1, ShutdownTick = -1;

            protected override UniTask OnInitializeAsync()
            {
                Data = new TestStoreData();
                InitCalled = true;
                InitTick = s_clock++;
                return UniTask.CompletedTask;
            }

            protected override void OnShutdown()
            {
                ShutdownCalled = true;
                ShutdownTick = s_clock++;
            }
        }

        private sealed class TestAsyncStore : StoreBase<TestStoreData>
        {
            public bool InitCalled;

            protected override UniTask OnInitializeAsync()
            {
                InitCalled = true;
                Data = new TestStoreData();
                return UniTask.CompletedTask;
            }
        }

        private sealed class NoopProcedure : ProcedureBase
        {
            protected override UniTask OnExecuteAsync(CancellationToken ct)
                => UniTask.CompletedTask;
        }

        private sealed class SubscribingProcedure : ProcedureBase
        {
            public int ReceivedCount;

            protected override UniTask OnExecuteAsync(CancellationToken ct)
            {
                Subscribe<TestEvent>(OnEvent);
                ArchContext.Current.Event.Publish(new TestEvent());
                return UniTask.CompletedTask;
            }

            private void OnEvent(TestEvent e) => ReceivedCount++;
        }

        private sealed class ThrowingProcedure : ProcedureBase
        {
            public int ReceivedCount;

            protected override UniTask OnExecuteAsync(CancellationToken ct)
            {
                Subscribe<TestEvent>(OnEvent);
                throw new InvalidOperationException("boom");
            }

            private void OnEvent(TestEvent e) => ReceivedCount++;
        }

        private sealed class TestSingletonView : GameView, ISingletonView { }

        #endregion
    }
}
