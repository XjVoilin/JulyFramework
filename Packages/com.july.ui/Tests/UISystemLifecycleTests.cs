using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Resource;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class UISystemLifecycleTests
    {
        private const int WindowId = 91001;

        private readonly struct TestSignal { }

        private sealed class TestObserverSystem : SystemBase
        {
            internal Action<UIOpenEvent> Opened;

            protected override UniTask OnInitializeAsync()
            {
                Subscribe<UIOpenEvent>(e => Opened?.Invoke(e));
                return UniTask.CompletedTask;
            }

            internal void PublishSignal() => Publish(new TestSignal());
        }

        private sealed class TestView : UIView
        {
            internal static bool CloseDuringBeforeOpen;
            internal static int SignalCount;
            internal static int AfterCloseCount;
            internal static TestView LastInstance;

            internal static void Reset()
            {
                CloseDuringBeforeOpen = false;
                SignalCount = 0;
                AfterCloseCount = 0;
                LastInstance = null;
            }

            protected override void OnBeforeOpen()
            {
                LastInstance = this;
                this.Subscribe<TestSignal>(_ => SignalCount++);
                if (CloseDuringBeforeOpen)
                    CloseWindow();
            }

            protected override void OnAfterClose()
            {
                AfterCloseCount++;
            }
        }

        private sealed class DelayedResourceSystem : SystemBase, IResourceSystem
        {
            private readonly GameObject _prefab;
            private readonly UniTaskCompletionSource _loadGate = new();

            public int InstantiateCallCount { get; private set; }
            public bool FailNextLoad { get; set; }

            public DelayedResourceSystem(GameObject prefab)
            {
                _prefab = prefab;
            }

            public void ReleaseLoads() => _loadGate.TrySetResult();

            public UniTask<ResourceHandle<T>> LoadAssetAsync<T>(string fileName,
                CancellationToken ct = default) where T : UnityEngine.Object
                => throw new NotSupportedException();

            public UniTask<T> LoadAsync<T>(string fileName, GameObject bindTo,
                CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public UniTask<TResult> LoadScopedAsync<T, TResult>(string fileName, Func<T, TResult> use,
                CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public UniTask<ResourceHandle<T>[]> LoadBatchAsync<T>(IReadOnlyList<string> fileNames,
                CancellationToken ct = default) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public bool HasAsset(string fileName) => true;

            public async UniTask<GameObject> InstantiateAsync(string fileName, Transform parent = null,
                CancellationToken ct = default)
            {
                InstantiateCallCount++;
                await _loadGate.Task.AttachExternalCancellation(ct);
                if (FailNextLoad)
                {
                    FailNextLoad = false;
                    return null;
                }
                return UnityEngine.Object.Instantiate(_prefab, parent);
            }

            public async UniTask<T> InstantiateAsync<T>(string fileName, Transform parent = null,
                CancellationToken ct = default) where T : Component
            {
                var instance = await InstantiateAsync(fileName, parent, ct);
                return instance != null ? instance.GetComponent<T>() : null;
            }

            public UniTask<bool> DownloadByTagAsync(string tag, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public UniTask<bool> DownloadByTagWithRetryAsync(string tag, int maxRetries = 3,
                CancellationToken ct = default) => throw new NotSupportedException();

            public UniTask UnloadUnusedAssetsAsync() => throw new NotSupportedException();

            public UniTask<Scene> LoadSceneAsync(string sceneName,
                LoadSceneMode mode = LoadSceneMode.Single, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public UniTask<bool> UnloadSceneAsync(string sceneName, CancellationToken ct = default) =>
                throw new NotSupportedException();
        }

        private ArchContext _context;
        private GameObject _prefab;
        private DelayedResourceSystem _resources;
        private TestObserverSystem _observer;
        private UISystem _ui;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DestroyExistingUIInfrastructure();
            yield return null;

            TestView.Reset();
            _prefab = new GameObject("LifecycleTestWindow", typeof(RectTransform), typeof(TestView));
            _resources = new DelayedResourceSystem(_prefab);
            _observer = new TestObserverSystem();
            _ui = new UISystem();
            _context = new ArchContext();
            _context.RegisterSystem(_resources);
            _context.RegisterSystem(_observer);
            _context.RegisterSystem(_ui);
            _context.InitializeAsync().GetAwaiter().GetResult();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _context?.Shutdown();
            _context = null;

            foreach (var view in UnityEngine.Object.FindObjectsOfType<TestView>(true))
            {
                if (view != null)
                    UnityEngine.Object.DestroyImmediate(view.gameObject);
            }

            _prefab = null;
            _resources = null;
            _observer = null;
            _ui = null;
            DestroyExistingUIInfrastructure();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConcurrentOpen_DuringDelayedLoad_UsesOneWindowLifecycle()
            => Run(async () =>
            {
                var options = CreateOptions();
                var firstOpen = _ui.OpenAsync(options);
                var secondOpen = _ui.OpenAsync(options);

                _resources.ReleaseLoads();
                var firstView = await firstOpen;
                var secondView = await secondOpen;

                await _ui.CloseAsync(WindowId);
                await _ui.CloseAsync(WindowId);

                Assert.That(firstView.IsOpened || secondView.IsOpened, Is.False,
                    "Closing a window id must leave no unaddressable opened instance.");
                Assert.That(_resources.InstantiateCallCount, Is.EqualTo(1),
                    "Concurrent opens must join one lifecycle instead of starting two loads.");
                Assert.That(secondView, Is.SameAs(firstView));
            });

        [UnityTest]
        public IEnumerator Close_DuringDelayedLoad_CancelsLifecycleAndAllowsReopen()
            => Run(async () =>
            {
                var opening = _ui.OpenAsync(CreateOptions());
                var closing = _ui.CloseAsync(WindowId);

                var openingWasCanceled = false;
                try
                {
                    await opening;
                }
                catch (OperationCanceledException)
                {
                    openingWasCanceled = true;
                }

                await closing;
                Assert.That(openingWasCanceled, Is.True);

                _resources.ReleaseLoads();
                var reopened = await _ui.OpenAsync(CreateOptions());

                Assert.That(reopened, Is.Not.Null);
                Assert.That(reopened.IsOpened, Is.True);
                Assert.That(_resources.InstantiateCallCount, Is.EqualTo(2));
                await _ui.CloseAsync(WindowId);
            });

        [UnityTest]
        public IEnumerator OpenFailure_RemovesLifecycleAndAllowsRetry()
            => Run(async () =>
            {
                _resources.FailNextLoad = true;
                _resources.ReleaseLoads();
                LogAssert.Expect(LogType.Error,
                    "[UISystem] Failed to load UI prefab: LifecycleTestWindow");

                var failed = await _ui.OpenAsync(CreateOptions());
                var retried = await _ui.OpenAsync(CreateOptions());

                Assert.That(failed, Is.Null);
                Assert.That(retried, Is.Not.Null);
                Assert.That(retried.IsOpened, Is.True);
                Assert.That(_resources.InstantiateCallCount, Is.EqualTo(2));
                await _ui.CloseAsync(WindowId);
            });

        [UnityTest]
        public IEnumerator StaleView_CannotCloseReplacementLifecycle()
            => Run(async () =>
            {
                _resources.ReleaseLoads();
                var firstView = await _ui.OpenAsync(CreateOptions());
                await _ui.CloseAsync(firstView);

                var replacementView = await _ui.OpenAsync(CreateOptions());
                await _ui.CloseAsync(firstView);

                Assert.That(replacementView, Is.Not.SameAs(firstView));
                Assert.That(replacementView.IsOpened, Is.True,
                    "A stale view handle must not close the current lifecycle with the same window id.");
                await _ui.CloseAsync(replacementView);
            });

        [UnityTest]
        public IEnumerator QueuedSameWindowId_WaitsForPreviousLifecycleToClose()
            => Run(async () =>
            {
                var options = CreateOptions();
                options.QueueMode = UIQueueMode.Enqueue;
                _resources.ReleaseLoads();

                var firstView = await _ui.OpenAsync(options);
                var queuedOpen = _ui.OpenAsync(options);

                Assert.That(_resources.InstantiateCallCount, Is.EqualTo(1));

                await _ui.CloseAsync(firstView);
                var replacementView = await queuedOpen;

                Assert.That(replacementView, Is.Not.SameAs(firstView));
                Assert.That(replacementView.IsOpened, Is.True);
                Assert.That(_resources.InstantiateCallCount, Is.EqualTo(2));

                await _ui.CloseAsync(replacementView);
            });

        [UnityTest]
        public IEnumerator CloseFromBeforeOpen_CancelsAndCleansPreparedView()
            => Run(async () =>
            {
                TestView.CloseDuringBeforeOpen = true;
                _resources.ReleaseLoads();

                var canceled = false;
                try
                {
                    await _ui.OpenAsync(CreateOptions());
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                _observer.PublishSignal();
                Assert.That(canceled, Is.True);
                Assert.That(TestView.SignalCount, Is.Zero,
                    "Subscriptions created in OnBeforeOpen must be removed when opening is canceled.");
                Assert.That(TestView.AfterCloseCount, Is.EqualTo(1));
            });

        [UnityTest]
        public IEnumerator OpenEvent_ObservesOpenedState_AndMayCloseReentrantly()
            => Run(async () =>
            {
                var eventSawOpenedView = false;
                _observer.Opened = _ =>
                {
                    eventSawOpenedView = TestView.LastInstance != null
                                         && TestView.LastInstance.IsOpened;
                    _ui.Close(TestView.LastInstance);
                };
                _resources.ReleaseLoads();

                var view = await _ui.OpenAsync(CreateOptions());

                Assert.That(eventSawOpenedView, Is.True);
                Assert.That(view.IsOpened, Is.False);
            });

        private static UIOpenOptions CreateOptions() => new()
        {
            WindowIdentifier = new WindowIdentifier(WindowId, "LifecycleTestWindow"),
            OpenAnimationType = UIAnimationType.None,
            CloseAnimationType = UIAnimationType.None,
        };

        private static IEnumerator Run(Func<UniTask> test) => test().ToCoroutine();

        private static void DestroyExistingUIInfrastructure()
        {
            foreach (var eventSystem in UnityEngine.Object.FindObjectsOfType<EventSystem>(true))
            {
                if (eventSystem != null)
                    UnityEngine.Object.DestroyImmediate(eventSystem.gameObject);
            }

            DestroyByName("[UIRoot]");
            DestroyByName("[UI_Staging]");
            DestroyByName("[UI Mask]");
        }

        private static void DestroyByName(string objectName)
        {
            foreach (var transform in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (transform != null && transform.name == objectName)
                    UnityEngine.Object.DestroyImmediate(transform.gameObject);
            }
        }
    }
}
