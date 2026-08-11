using System;
using System.Collections.Generic;
using July.Logging;
using July.Resource;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace July.UI
{
    /// <summary>
    /// Toast Tip 管理器 — 由 UISystem 持有，负责 Tip 对象池、容器、显示逻辑。
    /// 首次 ShowTip 时懒加载预制体，加载期间的消息会在资源就绪后按顺序显示。
    /// </summary>
    internal sealed class TipManager
    {
        private TipConfig _config;
        private readonly Func<IResourceSystem> _resourceResolver;

        private Transform _container;
        private GameObject _tipPrefab;
        private IDisposable _prefabHandle;

        private readonly Queue<UITipItem> _pool = new();
        private readonly List<UITipItem> _activeTips = new();
        private readonly Queue<TipRequest> _pendingTips = new();

        private bool _loading;
        private bool _initialized;

        internal TipManager(Func<IResourceSystem> resourceResolver)
        {
            _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
            _config = TipConfig.Default;
        }

        internal void Configure(TipConfig config)
        {
            if (_initialized && _config.TipPrefabPath != config.TipPrefabPath)
            {
                for (int i = 0; i < _activeTips.Count; i++)
                    if (_activeTips[i] != null) Object.Destroy(_activeTips[i].gameObject);
                _activeTips.Clear();

                while (_pool.Count > 0)
                {
                    var item = _pool.Dequeue();
                    if (item != null) Object.Destroy(item.gameObject);
                }

                _prefabHandle?.Dispose();
                _prefabHandle = null;
                _tipPrefab = null;
                _initialized = false;
            }

            _config = config;
        }

        internal void Initialize()
        {
            CreateContainer();
        }

        internal void Show(string message, float duration = 0)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (!_initialized)
            {
                _pendingTips.Enqueue(new TipRequest(message, duration));
                if (!_loading) LoadPrefabAsync();
                return;
            }

            ShowInitialized(message, duration);
        }

        private void ShowInitialized(string message, float duration)
        {
            for (int i = 0; i < _activeTips.Count; i++)
            {
                if (_activeTips[i] != null && _activeTips[i].Message == message)
                {
                    _activeTips[i].RestartTimer();
                    return;
                }
            }

            var tip = GetFromPool();
            if (tip == null) return;

            MoveUpExisting(tip.GetHeight() + _config.Spacing);

            var showDuration = duration > 0 ? duration : _config.DefaultDuration;
            tip.Show(message, showDuration, _config.FadeOutDuration, OnTipComplete,
                _config.EnterOffset, _config.EnterDuration);

            _activeTips.Add(tip);
        }

        internal void Shutdown()
        {
            _activeTips.Clear();
            _pool.Clear();
            _pendingTips.Clear();
            _prefabHandle?.Dispose();
            _prefabHandle = null;
            _tipPrefab = null;
            _initialized = false;
            _loading = false;

            if (_container != null)
            {
                Object.Destroy(_container.gameObject);
                _container = null;
            }
        }

        private void CreateContainer()
        {
            var go = new GameObject("[TipContainer]");
            Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            canvas.vertexColorAlwaysGammaSpace = true;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>().enabled = false;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0, 100);

            _container = go.transform;
        }

        private async void LoadPrefabAsync()
        {
            if (string.IsNullOrEmpty(_config.TipPrefabPath))
            {
                _pendingTips.Clear();
                return;
            }

            _loading = true;

            try
            {
                var resource = _resourceResolver();
                if (resource == null)
                {
                    JLogger.LogWarning("[TipManager] ResourceSystem not available");
                    _pendingTips.Clear();
                    _loading = false;
                    return;
                }

                var handle = await resource.LoadAssetAsync<GameObject>(_config.TipPrefabPath);
                if (handle == null || !handle.IsValid)
                {
                    JLogger.LogWarning($"[TipManager] Failed to load tip prefab: {_config.TipPrefabPath}");
                    _pendingTips.Clear();
                    _loading = false;
                    return;
                }

                _prefabHandle = handle.MarkPermanent();
                _tipPrefab = handle.Asset;
                WarmupPool();
                _initialized = true;
                ShowPendingTips();
            }
            catch (Exception ex)
            {
                JLogger.LogWarning($"[TipManager] Prefab load error: {ex.Message}");
                _pendingTips.Clear();
            }
            finally
            {
                _loading = false;
            }
        }

        private void ShowPendingTips()
        {
            while (_pendingTips.Count > 0)
            {
                var request = _pendingTips.Dequeue();
                ShowInitialized(request.Message, request.Duration);
            }
        }

        private void WarmupPool()
        {
            for (int i = 0; i < 2; i++)
            {
                var item = CreateItem();
                if (item != null)
                {
                    item.gameObject.SetActive(false);
                    _pool.Enqueue(item);
                }
            }
        }

        private UITipItem CreateItem()
        {
            if (_tipPrefab == null || _container == null) return null;

            var go = Object.Instantiate(_tipPrefab, _container);
            var tip = go.GetComponent<UITipItem>();
            if (tip == null)
                tip = go.AddComponent<UITipItem>();
            go.SetActive(false);
            return tip;
        }

        private UITipItem GetFromPool()
        {
            while (_pool.Count > 0)
            {
                var item = _pool.Dequeue();
                if (item != null) return item;
            }
            return CreateItem();
        }

        private void MoveUpExisting(float offset)
        {
            for (int i = 0; i < _activeTips.Count; i++)
            {
                var tip = _activeTips[i];
                if (tip != null && tip.gameObject.activeSelf)
                    tip.MoveUp(offset, _config.MoveUpDuration);
            }
        }

        private void OnTipComplete(UITipItem tip)
        {
            _activeTips.Remove(tip);

            if (tip == null) return;
            tip.gameObject.SetActive(false);
            tip.Reset();

            if (_pool.Count < _config.PoolMaxSize)
                _pool.Enqueue(tip);
            else
                Object.Destroy(tip.gameObject);
        }

        private readonly struct TipRequest
        {
            internal readonly string Message;
            internal readonly float Duration;

            internal TipRequest(string message, float duration)
            {
                Message = message;
                Duration = duration;
            }
        }
    }
}
