using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.UI.Tests
{
    [TestFixture]
    public class UIWindowSequencerTests
    {
        /// <summary>测试用 UIView 具体子类（UIView 无 abstract 成员，可直接实例化）。</summary>
        private sealed class TestView : UIView { }

        private UIWindowSequencer _sequencer;
        private readonly List<int> _opened = new();
        private readonly List<GameObject> _gos = new();

        private static UIOpenOptions MakeOptions(int id, UIQueueMode mode)
            => new UIOpenOptions
            {
                WindowIdentifier = new WindowIdentifier(id, id.ToString()),
                QueueMode = mode,
                OpenAnimationType = UIAnimationType.None,
                CloseAnimationType = UIAnimationType.None,
            };

        /// <summary>fake 打开者：成功时创建 TestView 并记录顺序，失败时返回 null。</summary>
        private sealed class FakeOpener : IUIWindowOpener
        {
            private readonly List<int> _opened;
            private readonly List<GameObject> _gos;
            private readonly bool _success;

            public FakeOpener(List<int> opened, List<GameObject> gos, bool success = true)
            {
                _opened = opened;
                _gos = gos;
                _success = success;
            }

            public UniTask<UIView> OpenCoreAsync(UIOpenOptions opts, CancellationToken ct)
            {
                if (!_success) return UniTask.FromResult<UIView>(null);
                var go = new GameObject($"View_{opts.WindowIdentifier.ID}");
                var view = go.AddComponent<TestView>();
                view.WindowId = opts.WindowIdentifier.ID;
                _gos.Add(go);
                _opened.Add(opts.WindowIdentifier.ID);
                return UniTask.FromResult<UIView>(view);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _opened.Clear();
            _sequencer = new UIWindowSequencer(new FakeOpener(_opened, _gos, success: true));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _gos.Clear();
        }

        [UnityTest]
        public IEnumerator Enqueue_Serial_Open_One_By_One_After_Close()
            => Run(async () =>
            {
                var a = await _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                Assert.IsNotNull(a);
                Assert.AreEqual(new[] { 1 }, _opened);

                var b = await _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);
                var c = await _sequencer.RequestAsync(MakeOptions(3, UIQueueMode.Enqueue), default);
                Assert.IsNull(b);
                Assert.IsNull(c);
                Assert.AreEqual(new[] { 1 }, _opened);

                _sequencer.OnWindowClosed(1);
                Assert.AreEqual(new[] { 1, 2 }, _opened);

                _sequencer.OnWindowClosed(2);
                Assert.AreEqual(new[] { 1, 2, 3 }, _opened);

                _sequencer.OnWindowClosed(3);
                Assert.AreEqual(new[] { 1, 2, 3 }, _opened);
            });

        [UnityTest]
        public IEnumerator EnqueueFirst_Inserts_At_Head()
            => Run(async () =>
            {
                await _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                await _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);
                await _sequencer.RequestAsync(MakeOptions(9, UIQueueMode.EnqueueFirst), default);

                _sequencer.OnWindowClosed(1);
                // U(9) 插队到队首，应先于 B(2) 打开
                Assert.AreEqual(new[] { 1, 9 }, _opened);

                _sequencer.OnWindowClosed(9);
                Assert.AreEqual(new[] { 1, 9, 2 }, _opened);
            });

        [UnityTest]
        public IEnumerator Clear_Removes_Pending_Without_Affecting_Active()
            => Run(async () =>
            {
                await _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                await _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);
                await _sequencer.RequestAsync(MakeOptions(3, UIQueueMode.Enqueue), default);

                _sequencer.Clear();
                _sequencer.OnWindowClosed(1);
                // 队列已清空，关闭 A 后不应再开 B/C
                Assert.AreEqual(new[] { 1 }, _opened);
            });

        [UnityTest]
        public IEnumerator SubWindow_Close_Does_Not_Advance_Queue()
            => Run(async () =>
            {
                await _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                await _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);

                // 模拟 A 开了子窗口 D(2002)，关闭 D 不应推进 B
                _sequencer.OnWindowClosed(2002);
                Assert.AreEqual(new[] { 1 }, _opened);

                // 关闭 A 才开 B
                _sequencer.OnWindowClosed(1);
                Assert.AreEqual(new[] { 1, 2 }, _opened);
            });

        [UnityTest]
        public IEnumerator OpenFailure_Skips_To_Next_Does_Not_Deadlock()
            => Run(async () =>
            {
                _sequencer = new UIWindowSequencer(new FakeOpener(_opened, _gos, success: false));
                // 打开失败 → _activeWindowId 回滚，不卡死
                await _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                await _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);
                Assert.IsEmpty(_opened);
            });

        [UnityTest]
        public IEnumerator Different_WindowIds_Serialize_Globally()
            => Run(async () =>
            {
                // 升级奖励(1001)、成就(1002)、领奖(1003) 交错，全局串行
                await _sequencer.RequestAsync(MakeOptions(1001, UIQueueMode.Enqueue), default);
                await _sequencer.RequestAsync(MakeOptions(1002, UIQueueMode.Enqueue), default);
                await _sequencer.RequestAsync(MakeOptions(1003, UIQueueMode.Enqueue), default);

                Assert.AreEqual(new[] { 1001 }, _opened);

                _sequencer.OnWindowClosed(1001);
                Assert.AreEqual(new[] { 1001, 1002 }, _opened);

                _sequencer.OnWindowClosed(1002);
                Assert.AreEqual(new[] { 1001, 1002, 1003 }, _opened);
            });

        private static IEnumerator Run(Func<UniTask> test) => test().ToCoroutine();
    }
}
