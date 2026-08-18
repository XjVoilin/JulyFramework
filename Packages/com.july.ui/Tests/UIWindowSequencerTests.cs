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
    public sealed class UIWindowSequencerTests
    {
        private sealed class TestView : UIView { }

        private UIWindowSequencer _sequencer;
        private readonly List<int> _opened = new();
        private readonly List<GameObject> _gameObjects = new();

        private static UIOpenOptions MakeOptions(int id, UIQueueMode mode,
            UILayer layer = UILayer.Normal) => new()
        {
            WindowIdentifier = new WindowIdentifier(id, id.ToString()),
            QueueMode = mode,
            Layer = layer,
            OpenAnimationType = UIAnimationType.None,
            CloseAnimationType = UIAnimationType.None,
        };

        private sealed class FakeOpener : IUIWindowOpener
        {
            private readonly List<int> _opened;
            private readonly List<GameObject> _gameObjects;

            internal FakeOpener(List<int> opened, List<GameObject> gameObjects)
            {
                _opened = opened;
                _gameObjects = gameObjects;
            }

            public UniTask<UIView> OpenCoreAsync(UIOpenOptions options, CancellationToken ct)
            {
                var go = new GameObject($"View_{options.WindowIdentifier.ID}");
                var view = go.AddComponent<TestView>();
                view.WindowId = options.WindowIdentifier.ID;
                _gameObjects.Add(go);
                _opened.Add(options.WindowIdentifier.ID);
                return UniTask.FromResult<UIView>(view);
            }
        }

        private sealed class ManualFirstOpener : IUIWindowOpener
        {
            private readonly FakeOpener _fallback;
            private readonly UniTaskCompletionSource<UIView> _first = new();
            private int _callCount;

            internal ManualFirstOpener(List<int> opened, List<GameObject> gameObjects)
            {
                _fallback = new FakeOpener(opened, gameObjects);
            }

            internal void FailFirst() => _first.TrySetResult(null);

            public UniTask<UIView> OpenCoreAsync(UIOpenOptions options, CancellationToken ct)
            {
                _callCount++;
                return _callCount == 1
                    ? _first.Task.AttachExternalCancellation(ct)
                    : _fallback.OpenCoreAsync(options, ct);
            }
        }

        private sealed class CancelOnceOpener : IUIWindowOpener
        {
            private readonly FakeOpener _fallback;
            private bool _cancelNext = true;

            internal CancelOnceOpener(List<int> opened, List<GameObject> gameObjects)
            {
                _fallback = new FakeOpener(opened, gameObjects);
            }

            public UniTask<UIView> OpenCoreAsync(UIOpenOptions options, CancellationToken ct)
            {
                if (_cancelNext)
                {
                    _cancelNext = false;
                    throw new OperationCanceledException();
                }
                return _fallback.OpenCoreAsync(options, ct);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _opened.Clear();
            _sequencer = new UIWindowSequencer(new FakeOpener(_opened, _gameObjects));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gameObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _gameObjects.Clear();
        }

        [UnityTest]
        public IEnumerator Enqueue_OpensOneByOne_AndEachTaskReturnsItsView()
            => Run(async () =>
            {
                var first = await _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                var secondTask = _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);
                var thirdTask = _sequencer.RequestAsync(MakeOptions(3, UIQueueMode.Enqueue), default);
                Assert.AreEqual(new[] { 1 }, _opened);

                _sequencer.OnWindowClosed(1);
                var second = await secondTask;
                Assert.That(second.WindowId, Is.EqualTo(2));
                Assert.AreEqual(new[] { 1, 2 }, _opened);

                _sequencer.OnWindowClosed(2);
                var third = await thirdTask;
                Assert.That(third.WindowId, Is.EqualTo(3));
                Assert.AreEqual(new[] { 1, 2, 3 }, _opened);

                _sequencer.OnWindowClosed(3);
                Assert.That(first.WindowId, Is.EqualTo(1));
            });

        [UnityTest]
        public IEnumerator EnqueueFirst_InsertsAtHead()
            => Run(async () =>
            {
                await _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                var secondTask = _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);
                var priorityTask = _sequencer.RequestAsync(MakeOptions(9, UIQueueMode.EnqueueFirst), default);

                _sequencer.OnWindowClosed(1);
                var priority = await priorityTask;
                Assert.That(priority.WindowId, Is.EqualTo(9));
                Assert.AreEqual(new[] { 1, 9 }, _opened);

                _sequencer.OnWindowClosed(9);
                var second = await secondTask;
                Assert.That(second.WindowId, Is.EqualTo(2));
            });

        [UnityTest]
        public IEnumerator Clear_CancelsPendingWithoutAffectingActive()
            => Run(async () =>
            {
                await _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                var pending = _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);

                _sequencer.Clear();
                var canceled = await IsCanceled(pending);
                _sequencer.OnWindowClosed(1);

                Assert.That(canceled, Is.True);
                Assert.AreEqual(new[] { 1 }, _opened);
            });

        [UnityTest]
        public IEnumerator ClearLayer_CancelsOnlyMatchingPendingRequests()
            => Run(async () =>
            {
                await _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                var popup = _sequencer.RequestAsync(
                    MakeOptions(2, UIQueueMode.Enqueue, UILayer.Popup), default);
                var normal = _sequencer.RequestAsync(
                    MakeOptions(3, UIQueueMode.Enqueue, UILayer.Normal), default);

                _sequencer.ClearLayer(UILayer.Popup);
                Assert.That(await IsCanceled(popup), Is.True);

                _sequencer.OnWindowClosed(1);
                var normalView = await normal;
                Assert.That(normalView.WindowId, Is.EqualTo(3));
                Assert.AreEqual(new[] { 1, 3 }, _opened);
            });

        [UnityTest]
        public IEnumerator SubWindowClose_DoesNotAdvanceQueue()
            => Run(async () =>
            {
                await _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                var secondTask = _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);

                _sequencer.OnWindowClosed(2002);
                Assert.AreEqual(new[] { 1 }, _opened);

                _sequencer.OnWindowClosed(1);
                await secondTask;
                Assert.AreEqual(new[] { 1, 2 }, _opened);
            });

        [UnityTest]
        public IEnumerator OpenFailure_AdvancesToQueuedRequest()
            => Run(async () =>
            {
                var opener = new ManualFirstOpener(_opened, _gameObjects);
                _sequencer = new UIWindowSequencer(opener);
                var failedTask = _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default);
                var nextTask = _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);

                opener.FailFirst();
                var failed = await failedTask;
                var next = await nextTask;

                Assert.That(failed, Is.Null);
                Assert.That(next.WindowId, Is.EqualTo(2));
                Assert.AreEqual(new[] { 2 }, _opened);
            });

        [UnityTest]
        public IEnumerator OpenCancellation_ReleasesActiveSlot()
            => Run(async () =>
            {
                _sequencer = new UIWindowSequencer(new CancelOnceOpener(_opened, _gameObjects));

                var canceled = await IsCanceled(
                    _sequencer.RequestAsync(MakeOptions(1, UIQueueMode.Enqueue), default));
                var next = await _sequencer.RequestAsync(MakeOptions(2, UIQueueMode.Enqueue), default);

                Assert.That(canceled, Is.True);
                Assert.That(next.WindowId, Is.EqualTo(2));
                Assert.AreEqual(new[] { 2 }, _opened);
            });

        [UnityTest]
        public IEnumerator DifferentWindowIds_SerializeGlobally()
            => Run(async () =>
            {
                await _sequencer.RequestAsync(MakeOptions(1001, UIQueueMode.Enqueue), default);
                var second = _sequencer.RequestAsync(MakeOptions(1002, UIQueueMode.Enqueue), default);
                var third = _sequencer.RequestAsync(MakeOptions(1003, UIQueueMode.Enqueue), default);

                Assert.AreEqual(new[] { 1001 }, _opened);
                _sequencer.OnWindowClosed(1001);
                await second;
                _sequencer.OnWindowClosed(1002);
                await third;

                Assert.AreEqual(new[] { 1001, 1002, 1003 }, _opened);
            });

        private static async UniTask<bool> IsCanceled(UniTask<UIView> task)
        {
            try
            {
                await task;
                return false;
            }
            catch (OperationCanceledException)
            {
                return true;
            }
        }

        private static IEnumerator Run(Func<UniTask> test) => test().ToCoroutine();
    }
}
