using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;
using July.Resource;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace July.UI
{
    public class UISystem : SystemBase, IUISystem, IUIWindowOpener
    {
        private readonly Dictionary<int, UIWindowSession> _windows = new();
        private TipManager _tipManager;
        private UIWindowSequencer _sequencer;

        #region UIRoot Physical Stage

        private GameObject _uiRootGo;
        private Camera _uiCamera;
        private Transform _stagingRoot;
        private readonly Dictionary<UILayer, Transform> _layerTransforms = new();
        private readonly Dictionary<UILayer, Transform> _safeAreaRoots = new();

        private GameObject _maskRoot;
        private bool _maskActive;

        public Camera UICamera => _uiCamera;

        private Transform GetLayer(UILayer layer)
        {
            if (_layerTransforms.TryGetValue(layer, out var t))
                return t;
            return CreateLayerRoot(layer);
        }

        private Transform GetSafeAreaRoot(UILayer layer)
        {
            if (_safeAreaRoots.TryGetValue(layer, out var t))
                return t;

            var layerTransform = GetLayer(layer);
            if (layerTransform == null) return null;

            var safeAreaGo = new GameObject("SafeArea");
            safeAreaGo.transform.SetParent(layerTransform, false);
            var safeRect = safeAreaGo.AddComponent<RectTransform>();
            safeRect.anchorMin = Vector2.zero;
            safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = Vector2.zero;
            safeRect.offsetMax = Vector2.zero;
            safeAreaGo.AddComponent<SafeAreaAdapter>();

            _safeAreaRoots[layer] = safeAreaGo.transform;
            return safeAreaGo.transform;
        }

        private Transform CreateLayerRoot(UILayer layer)
        {
            var layerGo = new GameObject($"Layer_{layer}");
            layerGo.transform.SetParent(_uiRootGo.transform, false);
            layerGo.layer = LayerMask.NameToLayer("UI");

            var canvas = layerGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _uiCamera;
            canvas.sortingOrder = (int)layer;
            canvas.planeDistance = _uiConfig.PlaneDistance;
            canvas.vertexColorAlwaysGammaSpace = true;

            var scaler = layerGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = (Vector2)_uiConfig.DesignResolution;
            scaler.matchWidthOrHeight = _uiConfig.ScreenMatchMode;

            layerGo.AddComponent<GraphicRaycaster>();

            _layerTransforms[layer] = layerGo.transform;
            return layerGo.transform;
        }

        public void ShowMask()
        {
            if (_maskRoot == null) CreateMask();
            if (_maskActive) return;
            _maskRoot.SetActive(true);
            _maskActive = true;
        }

        public void HideMask()
        {
            if (!_maskActive) return;
            if (_maskRoot != null) _maskRoot.SetActive(false);
            _maskActive = false;
        }

        private void CreateUIRoot()
        {
            var existingRoot = GameObject.Find("[UIRoot]");
            if (existingRoot != null)
            {
                _uiRootGo = existingRoot;
                _uiCamera = _uiRootGo.GetComponentInChildren<Camera>();
                var existingStaging = GameObject.Find("[UI_Staging]");
                _stagingRoot = existingStaging != null ? existingStaging.transform : null;
                if (_uiCamera != null && _stagingRoot != null)
                {
                    AdoptOrCreateEventSystem();
                    return;
                }
            }

            _uiRootGo = new GameObject("[UIRoot]");
            _uiRootGo.layer = LayerMask.NameToLayer("UI");
            Object.DontDestroyOnLoad(_uiRootGo);

            var cameraGo = new GameObject("UICamera");
            cameraGo.layer = LayerMask.NameToLayer("UI");
            cameraGo.transform.SetParent(_uiRootGo.transform, false);
            _uiCamera = cameraGo.AddComponent<Camera>();
            _uiCamera.clearFlags = CameraClearFlags.Depth;
            _uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
            _uiCamera.orthographic = true;
            _uiCamera.orthographicSize = _uiConfig.CameraOrthographicSize;
            _uiCamera.depth = _uiConfig.UICameraDepth;
            _uiCamera.nearClipPlane = _uiConfig.CameraNearClip;
            _uiCamera.farClipPlane = 1000f;

            var audioListener = cameraGo.GetComponent<AudioListener>();
            if (audioListener != null)
                Object.Destroy(audioListener);

            var stagingGo = new GameObject("[UI_Staging]");
            stagingGo.SetActive(false);
            Object.DontDestroyOnLoad(stagingGo);
            stagingGo.hideFlags = HideFlags.HideInHierarchy;
            _stagingRoot = stagingGo.transform;

            AdoptOrCreateEventSystem();
        }

        private void AdoptOrCreateEventSystem()
        {
            var eventSystem = EventSystem.current;
            GameObject eventSystemGo;

            if (eventSystem != null)
            {
                eventSystemGo = eventSystem.gameObject;
            }
            else
            {
                eventSystemGo = new GameObject("[EventSystem]");
                eventSystemGo.AddComponent<EventSystem>();
                eventSystemGo.AddComponent<StandaloneInputModule>();
            }

            eventSystemGo.transform.SetParent(_uiRootGo.transform, false);
        }

        private void CreateMask()
        {
            if (_maskRoot != null) return;

            _maskRoot = new GameObject("[UI Mask]");
            Object.DontDestroyOnLoad(_maskRoot);

            var canvas = _maskRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            _maskRoot.AddComponent<GraphicRaycaster>();

            var imageGo = new GameObject("Blocker");
            imageGo.transform.SetParent(_maskRoot.transform, false);

            var image = imageGo.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;

            var rect = imageGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _maskRoot.SetActive(false);
            _maskActive = false;
        }

        private void ShutdownUIRoot()
        {
            if (_maskRoot != null)
            {
                Object.Destroy(_maskRoot);
                _maskRoot = null;
            }
            _maskActive = false;

            if (_uiRootGo != null)
                Object.Destroy(_uiRootGo);
            if (_stagingRoot != null)
                Object.Destroy(_stagingRoot.gameObject);

            _uiRootGo = null;
            _uiCamera = null;
            _stagingRoot = null;
            _layerTransforms.Clear();
            _safeAreaRoots.Clear();
        }

        #endregion

        #region Configuration

        private UIConfig _uiConfig = UIConfig.Default;
        private TipConfig _tipConfig = TipConfig.Default;

        public void Configure(UIConfig config) => _uiConfig = config;

        public void ConfigureTip(TipConfig config)
        {
            _tipConfig = config;
            _tipManager?.Configure(config);
        }

        #endregion

        #region ISupportMultipleSource

        private IUIWindowProvider _mainProvider;
        private IUIWindowProvider _additionalProvider;

        public void SetMainProvider(IUIWindowProvider provider) => _mainProvider = provider;
        public void SetAdditionalProvider(IUIWindowProvider provider) => _additionalProvider = provider;

        public void UnsetAdditionalProvider(IUIWindowProvider provider)
        {
            if (_additionalProvider == provider) _additionalProvider = null;
        }

        #endregion

        #region Lifecycle

        protected override UniTask OnInitializeAsync()
        {
            CreateUIRoot();
            InitTipManager();
            _sequencer = new UIWindowSequencer(this);
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown()
        {
            _sequencer?.Shutdown();
            CloseAll();
            _sequencer = null;
            _mainProvider = null;
            _additionalProvider = null;
            _tipManager?.Shutdown();
            _tipManager = null;
            ShutdownUIRoot();
        }

        #endregion

        #region Open

        public void Open(int windowId, object data = null, CancellationToken ct = default)
        {
            OpenAsync(windowId, data, ct).Forget();
        }

        public UniTask<UIView> OpenAsync(int windowId, object data = null, CancellationToken ct = default)
        {
            var options = ResolveOptions(windowId);
            if (options == null) return UniTask.FromResult<UIView>(null);

            return OpenAsync(SnapshotOptions(options, data), ct);
        }

        internal UniTask<UIView> OpenAsync(UIOpenOptions options, CancellationToken ct = default)
        {
            if (!IsValidOptions(options)) return UniTask.FromResult<UIView>(null);
            var request = SnapshotOptions(options, options.Data);
            if (request.QueueMode != UIQueueMode.None)
                return _sequencer.RequestAsync(request, ct);
            return OpenCoreAsync(request, ct);
        }

        UniTask<UIView> IUIWindowOpener.OpenCoreAsync(UIOpenOptions options, CancellationToken ct)
            => OpenCoreAsync(options, ct);

        internal async UniTask<UIView> OpenCoreAsync(UIOpenOptions options, CancellationToken ct = default)
        {
            if (!IsValidOptions(options)) return null;

            var windowId = options.WindowIdentifier.ID;
            while (_windows.TryGetValue(windowId, out var existing))
            {
                if (existing.Lifecycle is UIWindowLifecycle.Closing or UIWindowLifecycle.Closed)
                {
                    await existing.WaitUntilClosedAsync(ct);
                    continue;
                }

                if (existing.Lifecycle == UIWindowLifecycle.Open)
                {
                    JLogger.LogWarning($"[UISystem] Window {windowId} already open, reusing");
                    return existing.View;
                }

                return await existing.WaitUntilOpenedAsync(ct);
            }

            var session = new UIWindowSession(options, ct);
            _windows.Add(windowId, session);
            return await OpenSessionAsync(session);
        }

        private async UniTask<UIView> OpenSessionAsync(UIWindowSession session)
        {
            var options = session.Options;
            var ct = session.OpeningToken;

            try
            {
                var go = await InstantiateWindow(options.WindowIdentifier.WindowName, ct);
                if (go == null)
                {
                    ct.ThrowIfCancellationRequested();
                    session.CompleteOpening(null);
                    FinalizeSession(session, publishClose: false);
                    return null;
                }

                if (!session.TryAttachGameObject(go))
                {
                    Object.Destroy(go);
                    ct.ThrowIfCancellationRequested();
                    return null;
                }
                ct.ThrowIfCancellationRequested();

                var view = go.GetComponent<UIView>();
                if (view == null)
                {
                    JLogger.LogError($"[UISystem] Prefab '{options.WindowIdentifier.WindowName}' missing UIView component");
                    session.CompleteOpening(null);
                    FinalizeSession(session, publishClose: false);
                    return null;
                }

                view.WindowId = session.WindowId;
                view.InternalSetData(options.Data);

                var canvasGroup = go.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = go.AddComponent<CanvasGroup>();
                session.SetView(view, canvasGroup);

                var parentTransform = GetSafeAreaRoot(options.Layer);
                go.transform.SetParent(parentTransform, false);

                if (options.IgnoreSafeArea)
                    ExpandToFullScreen(go.GetComponent<RectTransform>());

                if (options.ShowMask)
                    RequestMask(session, parentTransform, options.ClickMaskToClose, go.transform);

                if (canvasGroup != null)
                {
                    canvasGroup.interactable = false;
                    canvasGroup.blocksRaycasts = false;
                }

                view.InternalBeforeOpen();

                var strategy = GetAnimationStrategy(options.OpenAnimationType);
                await strategy.PlayAsync(go, true, ct);
                ct.ThrowIfCancellationRequested();

                if (canvasGroup != null)
                {
                    canvasGroup.interactable = true;
                    canvasGroup.blocksRaycasts = true;
                }

                session.MarkOpened();
                view.InternalOpen();

                if (session.Lifecycle == UIWindowLifecycle.Open)
                {
                    this.Publish(new UIOpenEvent(session.WindowId, options.WindowIdentifier.WindowName,
                        options.Layer, options.Data));
                }
                session.CompleteOpening(view);

                return view;
            }
            catch (Exception ex)
            {
                session.FailOpening(ex);
                FinalizeSession(session, publishClose: false);
                throw;
            }
        }

        #endregion

        #region Close

        public void Close(int windowId)
        {
            CloseInternal(windowId).Forget();
        }

        public void Close(UIView view)
        {
            if (!TryGetCurrentSession(view, out var session)) return;
            CloseSessionAsync(session).Forget();
        }

        public UniTask CloseAsync(int windowId, CancellationToken ct = default)
        {
            return CloseInternal(windowId, ct);
        }

        public UniTask CloseAsync(UIView view, CancellationToken ct = default)
        {
            if (!TryGetCurrentSession(view, out var session)) return UniTask.CompletedTask;
            return CloseSessionAsync(session, ct);
        }

        private bool TryGetCurrentSession(UIView view, out UIWindowSession session)
        {
            session = null;
            if (view == null || !_windows.TryGetValue(view.WindowId, out var current)
                             || !ReferenceEquals(current.View, view))
                return false;
            session = current;
            return true;
        }

        private void CloseAll()
        {
            _sequencer?.Clear();
            var sessions = new List<UIWindowSession>(_windows.Values);
            foreach (var session in sessions)
                CloseImmediate(session);
        }

        public void CloseLayer(UILayer layer, int excludeWindowId = -1)
        {
            _sequencer?.ClearLayer(layer, excludeWindowId);
            var sessions = new List<UIWindowSession>();
            foreach (var kvp in _windows)
            {
                if (kvp.Value.Options.Layer == layer && kvp.Key != excludeWindowId)
                    sessions.Add(kvp.Value);
            }
            foreach (var session in sessions)
                CloseImmediate(session);
        }

        #endregion

        #region Tip

        public void ShowTip(string message, float duration = 2f)
        {
            if (_tipManager == null)
            {
                JLogger.LogWarning($"[UISystem] TipManager not initialized: {message}");
                return;
            }
            _tipManager.Show(message, duration);
        }

        private void InitTipManager()
        {
            _tipManager = new TipManager(() => GetSystem<IResourceSystem>());
            _tipManager.Configure(_tipConfig);
            _tipManager.Initialize();
        }

        #endregion

        #region Internal

        private UIOpenOptions ResolveOptions(int windowId)
        {
            if (_additionalProvider != null && _additionalProvider.TryResolve(windowId, out var opt))
                return opt;
            if (_mainProvider != null && _mainProvider.TryResolve(windowId, out opt))
                return opt;
            JLogger.LogError($"[UISystem] No config for windowId: {windowId}");
            return null;
        }

        private static UIOpenOptions SnapshotOptions(UIOpenOptions source, object data)
        {
            return new UIOpenOptions
            {
                WindowIdentifier = source.WindowIdentifier,
                Layer = source.Layer,
                QueueMode = source.QueueMode,
                Data = data,
                OpenAnimationType = source.OpenAnimationType,
                CloseAnimationType = source.CloseAnimationType,
                ShowMask = source.ShowMask,
                ClickMaskToClose = source.ClickMaskToClose,
                IgnoreSafeArea = source.IgnoreSafeArea,
            };
        }

        private static bool IsValidOptions(UIOpenOptions options)
        {
            if (options?.WindowIdentifier != null
                && !string.IsNullOrWhiteSpace(options.WindowIdentifier.WindowName))
                return true;

            JLogger.LogError("[UISystem] Invalid open options: WindowIdentifier and WindowName are required");
            return false;
        }

        private async UniTask CloseInternal(int windowId, CancellationToken ct = default)
        {
            if (!_windows.TryGetValue(windowId, out var session)) return;
            await CloseSessionAsync(session, ct);
        }

        private async UniTask CloseSessionAsync(UIWindowSession session, CancellationToken ct = default)
        {
            if (!session.TryBeginClosing(out var wasOpen))
            {
                await session.WaitUntilClosedAsync(ct);
                return;
            }

            if (!wasOpen)
            {
                await session.WaitUntilClosedAsync(ct);
                return;
            }

            try
            {
                SetInteractable(session.CanvasGroup, false);
                session.View?.InternalClose();
                var strategy = GetAnimationStrategy(session.Options.CloseAnimationType);
                await strategy.PlayAsync(session.GameObject, false, ct);
            }
            finally
            {
                FinalizeSession(session, publishClose: true);
            }
        }

        private void CloseImmediate(UIWindowSession session)
        {
            session.TryBeginClosing(out _);
            if (!session.WasOpened)
                session.FailOpening(new OperationCanceledException(session.OpeningToken));
            FinalizeSession(session, publishClose: session.WasOpened);
        }

        private void FinalizeSession(UIWindowSession session, bool publishClose)
        {
            if (!session.TryFinalize()) return;

            var view = session.View;
            if (view != null)
            {
                try
                {
                    if (view.IsOpened)
                        view.InternalClose();
                }
                catch (Exception ex)
                {
                    JLogger.LogError($"[UISystem] Window {session.WindowId} OnClose failed: {ex}");
                }

                try
                {
                    view.InternalAfterClose();
                }
                catch (Exception ex)
                {
                    JLogger.LogError($"[UISystem] Window {session.WindowId} OnAfterClose failed: {ex}");
                }
            }

            ReleaseMask(session);

            if (session.GameObject != null)
                Object.Destroy(session.GameObject);

            if (_windows.TryGetValue(session.WindowId, out var current)
                && ReferenceEquals(current, session))
                _windows.Remove(session.WindowId);

            if (publishClose)
            {
                this.Publish(new UICloseEvent(session.WindowId,
                    session.Options.WindowIdentifier.WindowName, session.Options.Layer));
            }
            session.CompleteClosed();
            if (publishClose)
                _sequencer?.OnWindowClosed(session.WindowId);
        }

        private static void ExpandToFullScreen(RectTransform windowRect)
        {
            if (windowRect == null) return;

            var safeAreaRect = windowRect.parent as RectTransform;
            if (safeAreaRect == null) return;

            var sMin = safeAreaRect.anchorMin;
            var sMax = safeAreaRect.anchorMax;

            float safeW = sMax.x - sMin.x;
            float safeH = sMax.y - sMin.y;
            if (safeW <= 0 || safeH <= 0) return;

            windowRect.anchorMin = new Vector2(-sMin.x / safeW, -sMin.y / safeH);
            windowRect.anchorMax = new Vector2((1f - sMin.x) / safeW, (1f - sMin.y) / safeH);
            windowRect.offsetMin = Vector2.zero;
            windowRect.offsetMax = Vector2.zero;
        }

        private static IUIAnimationStrategy GetAnimationStrategy(UIAnimationType type)
        {
            return type switch
            {
                UIAnimationType.Animator => AnimatorAnimationStrategy.Instance,
                UIAnimationType.Fade => FadeAnimationStrategy.Instance,
                UIAnimationType.Scale => ScaleAnimationStrategy.Instance,
                UIAnimationType.SlideFromTop => SlideAnimationStrategy.FromTop,
                UIAnimationType.SlideFromBottom => SlideAnimationStrategy.FromBottom,
                UIAnimationType.SlideFromLeft => SlideAnimationStrategy.FromLeft,
                UIAnimationType.SlideFromRight => SlideAnimationStrategy.FromRight,
                _ => NoneAnimationStrategy.Instance
            };
        }

        private async UniTask<GameObject> InstantiateWindow(string windowName, CancellationToken ct)
        {
            var resource = GetSystem<IResourceSystem>();
            if (resource == null)
            {
                JLogger.LogError("[UISystem] ResourceSystem not registered");
                return null;
            }

            var instance = await resource.InstantiateAsync(windowName, _stagingRoot, ct);
            if (instance == null)
            {
                JLogger.LogError($"[UISystem] Failed to load UI prefab: {windowName}");
                return null;
            }
            return instance;
        }

        private static void SetInteractable(CanvasGroup canvasGroup, bool interactable)
        {
            if (canvasGroup == null) return;
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = interactable;
        }

        #endregion

        #region Window Mask

        private void RequestMask(UIWindowSession session, Transform parent, bool clickToClose,
            Transform windowTransform)
        {
            var maskObj = new GameObject("UIMask");
            maskObj.transform.SetParent(parent, false);

            var rect = maskObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(5000, 5000);
            rect.anchoredPosition = Vector2.zero;

            var image = maskObj.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, _uiConfig.MaskAlpha);

            if (clickToClose)
            {
                var button = maskObj.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => CloseIfCurrent(session));
            }

            maskObj.transform.SetSiblingIndex(windowTransform.GetSiblingIndex());
            session.Mask = maskObj;
        }

        private void CloseIfCurrent(UIWindowSession session)
        {
            if (_windows.TryGetValue(session.WindowId, out var current)
                && ReferenceEquals(current, session))
                CloseSessionAsync(session).Forget();
        }

        private static void ReleaseMask(UIWindowSession session)
        {
            if (session.Mask != null)
                Object.Destroy(session.Mask);
            session.Mask = null;
        }

        #endregion
    }
}
