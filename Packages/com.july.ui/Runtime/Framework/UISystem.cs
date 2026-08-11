using System;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly UILayer[] AllLayers = (UILayer[])Enum.GetValues(typeof(UILayer));

        private readonly List<UIInfo> _stack = new();
        private readonly Dictionary<int, UIInfo> _openWindows = new();
        private readonly Dictionary<int, GameObject> _windowMasks = new();
        private readonly Dictionary<string, ResourceHandle<GameObject>> _preloadedPrefabs = new();
        private TipManager _tipManager;
        private UIWindowSequencer _sequencer;

        #region UIRoot Physical Stage

        private GameObject _uiRootGo;
        private Camera _uiCamera;
        private Transform _stagingRoot;
        private readonly Dictionary<UILayer, Canvas> _layerCanvases = new();
        private readonly Dictionary<UILayer, Transform> _layerTransforms = new();
        private readonly Dictionary<UILayer, Transform> _safeAreaRoots = new();

        private GameObject _maskRoot;
        private bool _maskActive;

        public Camera UICamera => _uiCamera;
        public Transform StagingRoot => _stagingRoot;
        public bool IsMaskActive => _maskActive;

        public Transform GetLayer(UILayer layer)
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

        public Canvas GetLayerCanvas(UILayer layer)
        {
            if (!_layerCanvases.ContainsKey(layer))
                CreateLayerRoot(layer);
            _layerCanvases.TryGetValue(layer, out var c);
            return c;
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

            _layerCanvases[layer] = canvas;
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
            _layerCanvases.Clear();
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

        public IUIWindowProvider MainProvider { get; private set; }
        public IUIWindowProvider AdditionalProvider { get; private set; }

        public void SetMainProvider(IUIWindowProvider provider) => MainProvider = provider;
        public void SetAdditionalProvider(IUIWindowProvider provider) => AdditionalProvider = provider;

        public void UnsetAdditionalProvider(IUIWindowProvider provider)
        {
            if (AdditionalProvider == provider) AdditionalProvider = null;
        }

        #endregion

        #region Lifecycle

        protected override UniTask OnInitializeAsync()
        {
            CreateUIRoot();
            InitTipManager();
            _sequencer = new UIWindowSequencer(this);
            Subscribe<UICloseEvent>(e => _sequencer.OnWindowClosed(e.WindowId));
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown()
        {
            CloseAll(destroy: true);
            _sequencer?.Shutdown();
            _sequencer = null;
            ReleaseAllMasks();
            ReleaseAllPreloads();
            MainProvider = null;
            AdditionalProvider = null;
            _tipManager?.Shutdown();
            _tipManager = null;
            ShutdownUIRoot();
        }

        #endregion

        public UIOpenOptions GetWindowConfig(int windowId) => ResolveOptions(windowId);

        #region Open

        public void Open(int windowId, object data = null, CancellationToken ct = default)
        {
            OpenAsync(windowId, data, ct).Forget();
        }

        public async UniTask<UIView> OpenAsync(int windowId, object data = null, CancellationToken ct = default)
        {
            var options = ResolveOptions(windowId);
            if (options == null) return null;

            options.Data = data;
            return await OpenAsync(options, ct);
        }

        public UniTask<UIView> OpenAsync(UIOpenOptions options, CancellationToken ct = default)
        {
            if (options == null) return UniTask.FromResult<UIView>(null);
            if (options.QueueMode != UIQueueMode.None)
                return _sequencer.RequestAsync(options, ct);
            return OpenCoreAsync(options, ct);
        }

        UniTask<UIView> IUIWindowOpener.OpenCoreAsync(UIOpenOptions options, CancellationToken ct)
            => OpenCoreAsync(options, ct);

        internal async UniTask<UIView> OpenCoreAsync(UIOpenOptions options, CancellationToken ct = default)
        {
            if (options == null) return null;

            var windowId = options.WindowIdentifier.ID;
            if (_openWindows.ContainsKey(windowId))
            {
                JLogger.LogWarning($"[UISystem] Window {windowId} already open, ignoring");
                return _openWindows[windowId].View;
            }

            var go = await LoadWindowPrefab(options.WindowIdentifier.WindowName, ct);
            if (go == null) return null;

            var view = go.GetComponent<UIView>();
            if (view == null)
            {
                JLogger.LogError($"[UISystem] Prefab '{options.WindowIdentifier.WindowName}' missing UIView component");
                Object.Destroy(go);
                return null;
            }

            view.WindowId = windowId;
            view.InternalSetData(options.Data);

            var canvasGroup = go.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = go.AddComponent<CanvasGroup>();

            var parentTransform = GetSafeAreaRoot(options.Layer);
            go.transform.SetParent(parentTransform, false);

            if (options.IgnoreSafeArea)
                ExpandToFullScreen(go.GetComponent<RectTransform>());

            if (options.ShowMask)
                RequestMask(windowId, parentTransform, options.ClickMaskToClose, go.transform);

            var uiInfo = new UIInfo
            {
                View = view,
                WindowId = windowId,
                WindowIdentifier = options.WindowIdentifier,
                Layer = options.Layer,
                IgnoreSafeArea = options.IgnoreSafeArea,
                CanvasGroup = canvasGroup,
                CloseAnimationType = options.CloseAnimationType,
                QueueMode = options.QueueMode,
            };

            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            view.InternalBeforeOpen();

            var strategy = GetAnimationStrategy(options.OpenAnimationType);
            await strategy.PlayAsync(go, true, ct);

            if (canvasGroup != null)
            {
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            view.InternalOpen();

            if (options.AddToStack)
                _stack.Add(uiInfo);
            _openWindows[windowId] = uiInfo;

            this.Publish(new UIOpenEvent(windowId, options.WindowIdentifier.WindowName, options.Layer, options.Data));

            return view;
        }

        public async UniTask<UIOpenResult> TryOpenAsync(UIOpenOptions options,
            CancellationToken ct = default)
        {
            if (options == null)
                return UIOpenResult.Failure(UIOpenError.InvalidOptions, "UIOpenOptions不能为空");

            try
            {
                var view = await OpenAsync(options, ct);
                if (view == null)
                    return UIOpenResult.Failure(UIOpenError.OpenFailed,
                        $"UI打开失败: {options.WindowIdentifier}");

                return UIOpenResult.Success(view);
            }
            catch (OperationCanceledException)
            {
                return UIOpenResult.Failure(UIOpenError.Cancelled, "UI打开被取消");
            }
            catch (Exception ex)
            {
                return UIOpenResult.Failure(UIOpenError.OpenFailed,
                    $"UI打开失败: {ex.Message}", ex);
            }
        }

        #endregion

        #region Close

        public void Close(int windowId, bool destroy = true, UIAnimationType? animationType = null)
        {
            CloseInternal(windowId, destroy, animationType).Forget();
        }

        public void Close(UIView view, bool destroy = true, UIAnimationType? animationType = null)
        {
            if (view == null) return;
            Close(view.WindowId, destroy, animationType);
        }

        public UniTask CloseAsync(int windowId, bool destroy = true, UIAnimationType? animationType = null,
            CancellationToken ct = default)
        {
            return CloseInternal(windowId, destroy, animationType, ct);
        }

        public UniTask CloseAsync(UIView view, bool destroy = true, UIAnimationType? animationType = null,
            CancellationToken ct = default)
        {
            if (view == null) return UniTask.CompletedTask;
            return CloseAsync(view.WindowId, destroy, animationType, ct);
        }

        public void CloseAll(bool destroy = false)
        {
            var ids = new List<int>(_openWindows.Keys);
            foreach (var id in ids)
                CloseImmediate(id, destroy);
        }

        public void CloseLayer(UILayer layer, bool destroy = false, int excludeWindowId = -1)
        {
            var ids = new List<int>();
            foreach (var kvp in _openWindows)
            {
                if (kvp.Value.Layer == layer && kvp.Key != excludeWindowId)
                    ids.Add(kvp.Key);
            }
            foreach (var id in ids)
                CloseImmediate(id, destroy);
        }

        public bool GoBack()
        {
            if (_stack.Count == 0) return false;
            var top = _stack[_stack.Count - 1];
            Close(top.WindowId);
            return true;
        }

        #endregion

        #region Query

        public bool IsOpen(int windowId) => _openWindows.ContainsKey(windowId);

        public bool TryGet(int windowId, out UIView view)
        {
            if (_openWindows.TryGetValue(windowId, out var info))
            {
                view = info.View;
                return true;
            }
            view = null;
            return false;
        }

        public bool TryGetUIInfo(int windowId, out UIInfo info)
        {
            return _openWindows.TryGetValue(windowId, out info);
        }

        public int GetStackDepth() => _stack.Count;

        public int GetLayerUICount(UILayer layer)
        {
            return _openWindows.Count(kvp => kvp.Value.Layer == layer);
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

        #region Preload

        public async UniTask PreloadAsync(string windowName, CancellationToken ct = default)
        {
            if (_preloadedPrefabs.ContainsKey(windowName)) return;
            var resource = GetSystem<IResourceSystem>();
            if (resource == null) return;

            var handle = await resource.LoadAssetAsync<GameObject>(windowName, ct);
            if (handle != null && handle.IsValid)
                _preloadedPrefabs[windowName] = handle;
        }

        public async UniTask<bool[]> PreloadBatchAsync(string[] windowNames, CancellationToken ct = default)
        {
            if (windowNames == null || windowNames.Length == 0)
                return Array.Empty<bool>();

            var tasks = new UniTask<bool>[windowNames.Length];
            for (int i = 0; i < windowNames.Length; i++)
            {
                var name = windowNames[i];
                tasks[i] = SafePreloadAsync(name, ct);
            }
            return await UniTask.WhenAll(tasks);
        }

        private async UniTask<bool> SafePreloadAsync(string windowName, CancellationToken ct)
        {
            try
            {
                await PreloadAsync(windowName, ct);
                return true;
            }
            catch (Exception ex)
            {
                JLogger.LogWarning($"[UISystem] Preload failed: {windowName} — {ex.Message}");
                return false;
            }
        }

        public void ReleasePreload(string windowName)
        {
            if (_preloadedPrefabs.Remove(windowName, out var handle))
                handle.Dispose();
        }

        public bool IsPreloaded(string windowName)
        {
            return _preloadedPrefabs.TryGetValue(windowName, out var handle) && handle.IsValid;
        }

        private void ReleaseAllPreloads()
        {
            foreach (var handle in _preloadedPrefabs.Values)
                handle.Dispose();
            _preloadedPrefabs.Clear();
        }

        #endregion

        #region Sequencer

        public void ClearQueue() => _sequencer?.Clear();

        #endregion

        #region Internal

        private UIOpenOptions ResolveOptions(int windowId)
        {
            if (AdditionalProvider != null && AdditionalProvider.TryResolve(windowId, out var opt))
                return opt;
            if (MainProvider != null && MainProvider.TryResolve(windowId, out opt))
                return opt;
            JLogger.LogError($"[UISystem] No config for windowId: {windowId}");
            return null;
        }

        private async UniTask CloseInternal(int windowId, bool destroy,
            UIAnimationType? animationOverride = null, CancellationToken ct = default)
        {
            if (!_openWindows.TryGetValue(windowId, out var info)) return;

            info.SetInteractable(false);
            info.View.InternalClose();

            var animType = animationOverride ?? info.CloseAnimationType;
            var strategy = GetAnimationStrategy(animType);
            await strategy.PlayAsync(info.GameObject, false, ct);

            info.View.InternalAfterClose();

            ReleaseMask(windowId);

            RemoveFromStack(windowId);
            _openWindows.Remove(windowId);

            var windowName = info.WindowIdentifier?.WindowName;
            if (info.GameObject != null)
            {
                if (destroy)
                    Object.Destroy(info.GameObject);
                else
                    info.GameObject.SetActive(false);
            }

            this.Publish(new UICloseEvent(windowId, windowName, info.Layer, destroy));
        }

        private void CloseImmediate(int windowId, bool destroy)
        {
            if (!_openWindows.TryGetValue(windowId, out var info)) return;

            info.View.InternalClose();
            info.View.InternalAfterClose();

            ReleaseMask(windowId);

            RemoveFromStack(windowId);
            _openWindows.Remove(windowId);

            var windowName = info.WindowIdentifier?.WindowName;
            if (info.GameObject != null)
            {
                if (destroy)
                    Object.Destroy(info.GameObject);
                else
                    info.GameObject.SetActive(false);
            }

            this.Publish(new UICloseEvent(windowId, windowName, info.Layer, destroy));
        }

        private void RemoveFromStack(int windowId)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].WindowId == windowId)
                {
                    _stack.RemoveAt(i);
                    break;
                }
            }
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

        private async UniTask<GameObject> LoadWindowPrefab(string windowName, CancellationToken ct)
        {
            var resource = GetSystem<IResourceSystem>();
            if (resource == null)
            {
                JLogger.LogError("[UISystem] ResourceSystem not registered");
                return null;
            }

            ResourceHandle<GameObject> handle;
            if (_preloadedPrefabs.TryGetValue(windowName, out var preloaded) && preloaded.IsValid)
            {
                handle = preloaded;
            }
            else
            {
                handle = await resource.LoadAssetAsync<GameObject>(windowName, ct);
                if (handle != null && handle.IsValid)
                    _preloadedPrefabs[windowName] = handle;
            }

            if (handle == null || !handle.IsValid)
            {
                JLogger.LogError($"[UISystem] Failed to load UI prefab: {windowName}");
                return null;
            }

            var go = Object.Instantiate(handle.Asset, _stagingRoot);
            handle.BindTo(go);
            return go;
        }

        #endregion

        #region Window Mask

        private void RequestMask(int windowId, Transform parent, bool clickToClose, Transform windowTransform)
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
                var id = windowId;
                button.onClick.AddListener(() => Close(id));
            }

            maskObj.transform.SetSiblingIndex(windowTransform.GetSiblingIndex());
            _windowMasks[windowId] = maskObj;
        }

        private void ReleaseMask(int windowId)
        {
            if (_windowMasks.Remove(windowId, out var maskObj) && maskObj != null)
                Object.Destroy(maskObj);
        }

        private void ReleaseAllMasks()
        {
            foreach (var maskObj in _windowMasks.Values)
            {
                if (maskObj != null) Object.Destroy(maskObj);
            }
            _windowMasks.Clear();
        }

        #endregion
    }
}
