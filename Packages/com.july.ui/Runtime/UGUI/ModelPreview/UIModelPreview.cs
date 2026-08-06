using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Resource;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>
    /// 将多个 3D 模型渲染到当前 RawImage，并使每个模型的位置跟随对应的 UI 锚点。
    /// 锚点的数量和布局由调用方负责。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class UIModelPreview : GameView
    {
        private const float CameraDistance = 10f;
        private const float CameraOrthographicSize = 1f;
        private const float RuntimeSlotSpacing = 1000f;

        private static int _nextRuntimeSlot;

        private readonly List<LoadedModel> _loadedModels = new();

        private RawImage _output;
        private Camera _previewCamera;
        private RenderTexture _renderTexture;
        private int _requestVersion;

        /// <summary>清除当前内容并显示指定的模型。</summary>
        public async UniTask ShowAsync(IReadOnlyList<ModelPreviewTarget> targets)
        {
            EnsureInitialized();
            var assetNames = ValidateAndGetAssetNames(targets);
            Clear();

            var requestVersion = _requestVersion;
            var resourceSystem = GetSystem<IResourceSystem>()
                ?? throw new InvalidOperationException("IResourceSystem 尚未注册。");
            var destroyToken = this.GetCancellationTokenOnDestroy();
            ResourceHandle<GameObject>[] handles = null; 

            try
            {
                handles = await resourceSystem.LoadBatchAsync<GameObject>(assetNames, destroyToken);
                destroyToken.ThrowIfCancellationRequested();
                if (requestVersion != _requestVersion)
                    return;
                if (handles == null || handles.Length != targets.Count)
                    throw new InvalidOperationException("批量加载返回的模型数量与请求不一致。");

                for (var index = 0; index < targets.Count; index++)
                {
                    var target = targets[index];
                    var handle = handles[index];
                    if (handle == null || !handle.IsValid)
                        throw new InvalidOperationException(
                            $"无法加载预览模型：{target.ModelAssetName}");

                    var model = Instantiate(handle.Asset, _previewCamera.transform);
                    handle.BindTo(model);
                    handles[index] = null;

                    PrepareModel(model, target.DisplayScale);
                    _loadedModels.Add(new LoadedModel(model, target.Anchor));
                }

                RefreshPreview();
            }
            catch
            {
                if (requestVersion == _requestVersion)
                    Clear();
                throw;
            }
            finally
            {
                DisposeHandles(handles);
            }
        }

        /// <summary>清除当前显示的全部模型。</summary>
        public void Clear()
        {
            _requestVersion++;
            for (var index = 0; index < _loadedModels.Count; index++)
                Destroy(_loadedModels[index].Model);

            _loadedModels.Clear();
            _output.enabled = false;
            if (_previewCamera!=null)
            {
                _previewCamera.enabled = false;
            }
        }

        protected override void OnViewAwake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_output != null)
                return;

            _output = GetComponent<RawImage>();
            _output.enabled = false;
            CreatePreviewCamera();
        }

        protected override void OnViewEnable()
        {
            RefreshPreview();
        }

        protected override void OnViewDisable()
        {
            if (_previewCamera != null)
            {
                _previewCamera.enabled = false;
            }
        }

        protected override void OnViewDestroy()
        {
            Clear();
            ReleaseRenderTexture();
            if (_previewCamera!=null)
            {
                Destroy(_previewCamera.gameObject);
            }
        }

        private void RefreshPreview()
        {
            if (_loadedModels.Count == 0)
                return;
            if (!EnsureRenderTexture())
            {
                _output.enabled = false;
                _previewCamera.enabled = false;
                return;
            }

            var outputRect = _output.rectTransform.rect;
            var uiCamera = GetUICamera();
            for (var index = 0; index < _loadedModels.Count; index++)
                PlaceModel(_loadedModels[index], outputRect, uiCamera);

            _output.enabled = true;
            _previewCamera.enabled = isActiveAndEnabled;
        }

        private void CreatePreviewCamera()
        {
            var cameraObject = new GameObject($"[UIModelPreview {GetInstanceID()}]");
            // 同层的多个预览实例通过空间分区互相隔离，避免相机拍到其他实例的模型。
            cameraObject.transform.position =
                Vector3.right * (++_nextRuntimeSlot * RuntimeSlotSpacing) +
                Vector3.back * CameraDistance;
            DontDestroyOnLoad(cameraObject);

            _previewCamera = cameraObject.AddComponent<Camera>();
            _previewCamera.enabled = false;
            _previewCamera.clearFlags = CameraClearFlags.SolidColor;
            _previewCamera.backgroundColor = Color.clear;
            _previewCamera.orthographic = true;
            _previewCamera.orthographicSize = CameraOrthographicSize;
        }

        private static string[] ValidateAndGetAssetNames(IReadOnlyList<ModelPreviewTarget> targets)
        {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));

            var assetNames = new string[targets.Count];
            for (var index = 0; index < targets.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(targets[index].ModelAssetName))
                    throw new ArgumentException($"第 {index} 个目标未指定模型资源。", nameof(targets));
                if (targets[index].Anchor == null)
                    throw new ArgumentException($"第 {index} 个目标未指定锚点。", nameof(targets));
                if (targets[index].DisplayScale <= 0f)
                    throw new ArgumentOutOfRangeException(
                        nameof(targets),
                        $"第 {index} 个目标的显示缩放必须大于零。");

                assetNames[index] = targets[index].ModelAssetName;
            }

            return assetNames;
        }

        private static void DisposeHandles(ResourceHandle<GameObject>[] handles)
        {
            if (handles == null)
                return;

            for (var index = 0; index < handles.Length; index++)
                handles[index]?.Dispose();
        }

        private void PrepareModel(GameObject model, float displayScale)
        {
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale *= displayScale;
        }

        private void PlaceModel(LoadedModel loadedModel, Rect outputRect, Camera uiCamera)
        {
            if (loadedModel.Anchor == null)
                return;

            var anchorCenter = loadedModel.Anchor.TransformPoint(loadedModel.Anchor.rect.center);
            var screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, anchorCenter);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _output.rectTransform,
                    screenPoint,
                    uiCamera,
                    out var outputPoint))
                return;

            var viewportPoint = new Vector3(
                Mathf.InverseLerp(outputRect.xMin, outputRect.xMax, outputPoint.x),
                Mathf.InverseLerp(outputRect.yMin, outputRect.yMax, outputPoint.y),
                CameraDistance);
            loadedModel.Model.transform.position =
                _previewCamera.ViewportToWorldPoint(viewportPoint);
        }

        private Camera GetUICamera()
        {
            var canvas = _output.canvas;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;

            return canvas.worldCamera;
        }

        private bool EnsureRenderTexture()
        {
            var requiredSize = GetRequiredTextureSize();
            if (requiredSize.x == 0 || requiredSize.y == 0)
                return false;

            if (_renderTexture != null &&
                _renderTexture.width == requiredSize.x &&
                _renderTexture.height == requiredSize.y)
                return true;

            ReleaseRenderTexture();
            _renderTexture = new RenderTexture(
                requiredSize.x,
                requiredSize.y,
                16,
                RenderTextureFormat.ARGB32)
            {
                name = $"UIModelPreview_{GetInstanceID()}"
            };
            _renderTexture.Create();

            _previewCamera.targetTexture = _renderTexture;
            _output.texture = _renderTexture;
            return true;
        }

        private Vector2Int GetRequiredTextureSize()
        {
            var rect = _output.rectTransform.rect;
            var scaleFactor = _output.canvas == null
                ? 1f
                : _output.canvas.scaleFactor;
            var maxTextureSize = SystemInfo.maxTextureSize;

            return new Vector2Int(
                Mathf.Clamp(Mathf.CeilToInt(rect.width * scaleFactor), 0, maxTextureSize),
                Mathf.Clamp(Mathf.CeilToInt(rect.height * scaleFactor), 0, maxTextureSize));
        }

        private void ReleaseRenderTexture()
        {
            if (_renderTexture == null)
                return;

            if (_previewCamera!=null)
            {
                _previewCamera.targetTexture = null;
            }
            _output.texture = null;
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        private readonly struct LoadedModel
        {
            public readonly GameObject Model;
            public readonly RectTransform Anchor;

            public LoadedModel(GameObject model, RectTransform anchor)
            {
                Model = model;
                Anchor = anchor;
            }
        }
    }
}
