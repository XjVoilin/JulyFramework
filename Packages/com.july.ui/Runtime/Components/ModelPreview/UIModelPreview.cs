using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Resource;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    public enum ModelPreviewAntiAliasing
    {
        Disabled = 1,
        TwoSamples = 2,
        FourSamples = 4,
    }

    /// <summary>
    /// 将多个 3D 模型渲染到当前 RawImage。
    /// 模型原点由当前预览实例配置，多个模型以相同间隔水平居中排列。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class UIModelPreview : GameView
    {
        private const float CameraDistance = 10f;
        private const float CameraNearClipPlane = 1f;
        private const float CameraFarClipPlane = 20f;
        private const float CameraOrthographicSize = 1f;
        private const float RuntimeSlotSpacing = 1000f;
        private const double RenderScheduleTolerance = 0.001d;
        private const int DepthBufferBits = 16;

        private static int _nextRuntimeSlot;

        private readonly List<LoadedModel> _loadedModels = new();

        [SerializeField, Min(0.01f)] private float _overallScale = 1f;
        [SerializeField, Range(0f, 1f)] private float _verticalAnchor;
        [SerializeField] private float _verticalOffset = 32f;
        [SerializeField, Min(0f)] private float _horizontalSpacing = 220f;

        [SerializeField, Range(0.25f, 1f)] private float _renderTextureScale = 0.7f;
        [SerializeField, Min(1)] private int _maxRenderFrameRate = 30;
        [SerializeField] private ModelPreviewAntiAliasing _antiAliasing =
            ModelPreviewAntiAliasing.TwoSamples;

        private RawImage _output;
        private Camera _previewCamera;
        private RenderTexture _renderTexture;
        private CancellationTokenSource _loadCts;
        private bool _renderingRequested;
        private double _nextRenderTime;

        /// <summary>清除当前内容并显示指定的模型。</summary>
        public async UniTask ShowAsync(
            IReadOnlyList<ModelPreviewTarget> targets,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            var assetNames = ValidateAndGetAssetNames(targets);
            ValidateLayout(targets.Count);
            Clear();

            var resourceSystem = GetSystem<IResourceSystem>();
            var loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loadCts = loadCts;
            var ct = loadCts.Token;
            ResourceHandle<GameObject>[] handles = null; 

            try
            {
                handles = await resourceSystem.LoadBatchAsync<GameObject>(assetNames, ct);
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

                    PrepareModel(model);
                    var loadedModel = new LoadedModel(model);
                    _loadedModels.Add(loadedModel);
                    target.ConfigureInstance?.Invoke(model);
                    loadedModel.CaptureReferenceScale();
                }

                RefreshPreview();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                // 新请求、Clear、Release 或销毁触发的正常取消。
            }
            catch
            {
                Clear();
                throw;
            }
            finally
            {
                DisposeHandles(handles);
                if (ReferenceEquals(_loadCts, loadCts))
                {
                    _loadCts.Dispose();
                    _loadCts = null;
                }
            }
        }

        /// <summary>
        /// 使用指定的实例级布局参数显示模型。传入值会直接覆盖当前组件的 Inspector 参数。
        /// </summary>
        public UniTask ShowAsync(
            IReadOnlyList<ModelPreviewTarget> targets,
            float overallScale,
            float verticalOffset,
            CancellationToken cancellationToken = default)
        {
            OverrideLayout(overallScale, verticalOffset);
            return ShowAsync(targets, cancellationToken);
        }

        /// <summary>
        /// 覆盖当前实例的渲染质量参数。写入后的值会直接显示在运行时 Inspector 中。
        /// </summary>
        public void OverrideRendering(
            float renderTextureScale,
            int maxRenderFrameRate,
            ModelPreviewAntiAliasing antiAliasing)
        {
            ValidateRenderingOverride(
                renderTextureScale,
                maxRenderFrameRate,
                antiAliasing);

            _renderTextureScale = renderTextureScale;
            _maxRenderFrameRate = maxRenderFrameRate;
            _antiAliasing = antiAliasing;
            RefreshLayout();
        }

        /// <summary>清除当前显示的全部模型。</summary>
        public void Clear()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;

            for (var index = 0; index < _loadedModels.Count; index++)
                Destroy(_loadedModels[index].Model);

            _loadedModels.Clear();
            _renderingRequested = false;
            if (_output != null)
            {
                _output.enabled = false;
            }
            if (_previewCamera!=null)
            {
                _previewCamera.enabled = false;
            }
        }

        /// <summary>清除当前模型并释放预览渲染纹理；组件之后仍可通过 ShowAsync 再次使用。</summary>
        public void Release()
        {
            Clear();
            ReleaseRenderTexture();
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

        private void LateUpdate()
        {
            if (!_renderingRequested || _previewCamera == null)
                return;

            var currentTime = Time.unscaledTimeAsDouble;
            if (currentTime + RenderScheduleTolerance < _nextRenderTime)
            {
                _previewCamera.enabled = false;
                return;
            }

            _previewCamera.enabled = true;
            _nextRenderTime = currentTime + 1d / _maxRenderFrameRate;
        }

        private void OnValidate()
        {
            RefreshLayout();
        }

        private void OnRectTransformDimensionsChange()
        {
            RefreshLayout();
        }

        protected override void OnViewDisable()
        {
            _renderingRequested = false;
            if (_previewCamera != null)
            {
                _previewCamera.enabled = false;
            }
        }

        protected override void OnViewDestroy()
        {
            Release();
            if (_previewCamera!=null)
            {
                Destroy(_previewCamera.gameObject);
            }
        }

        private void RefreshLayout()
        {
            if (_output == null || _loadedModels.Count == 0)
                return;

            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (_loadedModels.Count == 0)
                return;
            ValidateLayout(_loadedModels.Count);
            if (!EnsureRenderTexture())
            {
                _output.enabled = false;
                _previewCamera.enabled = false;
                return;
            }

            var outputRect = _output.rectTransform.rect;
            for (var index = 0; index < _loadedModels.Count; index++)
            {
                ApplyOverallScale(_loadedModels[index]);
                PlaceModel(
                    _loadedModels[index],
                    outputRect,
                    index,
                    _loadedModels.Count);
            }

            _output.enabled = true;
            _renderingRequested = isActiveAndEnabled;
            _nextRenderTime = 0d;
            _previewCamera.enabled = false;
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
            ConfigurePreviewCamera(_previewCamera);
        }

        internal static void ConfigurePreviewCamera(Camera camera)
        {
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.nearClipPlane = CameraNearClipPlane;
            camera.farClipPlane = CameraFarClipPlane;
            camera.orthographic = true;
            camera.orthographicSize = CameraOrthographicSize;
            camera.allowMSAA = true;
        }

        private static string[] ValidateAndGetAssetNames(IReadOnlyList<ModelPreviewTarget> targets)
        {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));

            var assetNames = new string[targets.Count];
            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                if (string.IsNullOrWhiteSpace(target.ModelAssetName))
                    throw new ArgumentException($"第 {index} 个目标未指定模型资源。", nameof(targets));
                assetNames[index] = target.ModelAssetName;
            }

            return assetNames;
        }

        private void ValidateLayout(int targetCount)
        {
            if (!IsFinite(_overallScale) || _overallScale <= 0f)
                throw new InvalidOperationException("模型预览的整体缩放必须大于零。");
            if (!IsFinite(_verticalAnchor) ||
                _verticalAnchor < 0f ||
                _verticalAnchor > 1f)
                throw new InvalidOperationException("模型预览的垂直锚点必须在 [0, 1] 范围内。");
            if (!IsFinite(_verticalOffset))
                throw new InvalidOperationException("模型预览的垂直偏移必须是有限数值。");
            if (!IsFinite(_horizontalSpacing) || _horizontalSpacing < 0f)
                throw new InvalidOperationException("模型预览的水平间隔必须是有限的非负数值。");
            if (targetCount > 1 && _horizontalSpacing <= 0f)
                throw new InvalidOperationException("显示多个模型时，模型预览的水平间隔必须大于零。");
            if (!IsFinite(_renderTextureScale) ||
                _renderTextureScale < 0.25f ||
                _renderTextureScale > 1f)
                throw new InvalidOperationException("模型预览的渲染纹理缩放必须在 [0.25, 1] 范围内。");
            if (_maxRenderFrameRate <= 0)
                throw new InvalidOperationException("模型预览的最大渲染帧率必须大于零。");
            if (!IsSupportedAntiAliasing(_antiAliasing))
                throw new InvalidOperationException("模型预览的 MSAA 配置无效。");
        }

        private static void ValidateRenderingOverride(
            float renderTextureScale,
            int maxRenderFrameRate,
            ModelPreviewAntiAliasing antiAliasing)
        {
            if (!IsFinite(renderTextureScale) ||
                renderTextureScale < 0.25f ||
                renderTextureScale > 1f)
                throw new ArgumentOutOfRangeException(
                    nameof(renderTextureScale),
                    "渲染纹理缩放必须在 [0.25, 1] 范围内。");
            if (maxRenderFrameRate <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maxRenderFrameRate),
                    "最大渲染帧率必须大于零。");
            if (!IsSupportedAntiAliasing(antiAliasing))
                throw new ArgumentOutOfRangeException(
                    nameof(antiAliasing),
                    "MSAA 只支持关闭、2 倍和 4 倍。");
        }

        private void OverrideLayout(float overallScale, float verticalOffset)
        {
            if (!IsFinite(overallScale) || overallScale <= 0f)
                throw new ArgumentOutOfRangeException(
                    nameof(overallScale),
                    "模型预览的整体缩放必须大于零。");
            if (!IsFinite(verticalOffset))
                throw new ArgumentOutOfRangeException(
                    nameof(verticalOffset),
                    "模型预览的垂直偏移必须是有限数值。");

            _overallScale = overallScale;
            _verticalOffset = verticalOffset;
            RefreshLayout();
        }

        private static void DisposeHandles(ResourceHandle<GameObject>[] handles)
        {
            if (handles == null)
                return;

            for (var index = 0; index < handles.Length; index++)
                handles[index]?.Dispose();
        }

        private static void PrepareModel(GameObject model)
        {
            model.transform.localPosition = Vector3.zero;
        }

        private void ApplyOverallScale(LoadedModel loadedModel)
        {
            loadedModel.Model.transform.localScale = CalculateModelScale(
                loadedModel.ReferenceScale,
                _overallScale);
        }

        internal static Vector3 CalculateModelScale(
            Vector3 referenceScale,
            float overallScale)
        {
            return referenceScale * overallScale;
        }

        private void PlaceModel(
            LoadedModel loadedModel,
            Rect outputRect,
            int index,
            int count)
        {
            var outputPoint = CalculateModelOrigin(
                outputRect,
                _verticalAnchor,
                _verticalOffset,
                _horizontalSpacing,
                index,
                count);
            var viewportPoint = new Vector3(
                Mathf.InverseLerp(outputRect.xMin, outputRect.xMax, outputPoint.x),
                Mathf.InverseLerp(outputRect.yMin, outputRect.yMax, outputPoint.y),
                CameraDistance);
            loadedModel.Model.transform.position =
                _previewCamera.ViewportToWorldPoint(viewportPoint);
        }

        internal static Vector2 CalculateModelOrigin(
            Rect outputRect,
            float verticalAnchor,
            float verticalOffset,
            float horizontalSpacing,
            int index,
            int count)
        {
            var centeredIndex = index - (count - 1) * 0.5f;
            return new Vector2(
                outputRect.center.x + centeredIndex * horizontalSpacing,
                Mathf.Lerp(outputRect.yMin, outputRect.yMax, verticalAnchor) +
                verticalOffset);
        }

        private bool EnsureRenderTexture()
        {
            var requiredSize = GetRequiredTextureSize();
            if (requiredSize.x == 0 || requiredSize.y == 0)
                return false;
            var descriptor = CreateRenderTextureDescriptor(
                requiredSize,
                (int)_antiAliasing);

            if (_renderTexture != null &&
                _renderTexture.width == requiredSize.x &&
                _renderTexture.height == requiredSize.y &&
                _renderTexture.depth == descriptor.depthBufferBits &&
                _renderTexture.antiAliasing == descriptor.msaaSamples)
                return true;

            ReleaseRenderTexture();
            _renderTexture = new RenderTexture(descriptor)
            {
                name = $"UIModelPreview_{GetInstanceID()}",
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false
            };
            _renderTexture.Create();

            _previewCamera.targetTexture = _renderTexture;
            _output.texture = _renderTexture;
            return true;
        }

        internal static RenderTextureDescriptor CreateRenderTextureDescriptor(
            Vector2Int size,
            int antiAliasingSamples)
        {
            var descriptor = new RenderTextureDescriptor(
                size.x,
                size.y,
                RenderTextureFormat.ARGB32,
                DepthBufferBits)
            {
                msaaSamples = antiAliasingSamples
            };
            descriptor.msaaSamples = Mathf.Max(
                1,
                SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor));
            return descriptor;
        }

        private Vector2Int GetRequiredTextureSize()
        {
            var rect = _output.rectTransform.rect;
            var scaleFactor = _output.canvas == null
                ? 1f
                : _output.canvas.scaleFactor;

            return UIModelPreviewTextureSizing.Calculate(
                rect.size,
                scaleFactor,
                _renderTextureScale,
                SystemInfo.maxTextureSize);
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

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsSupportedAntiAliasing(
            ModelPreviewAntiAliasing antiAliasing)
        {
            return antiAliasing is
                ModelPreviewAntiAliasing.Disabled or
                ModelPreviewAntiAliasing.TwoSamples or
                ModelPreviewAntiAliasing.FourSamples;
        }

        private sealed class LoadedModel
        {
            public readonly GameObject Model;
            public Vector3 ReferenceScale { get; private set; }

            public LoadedModel(GameObject model)
            {
                Model = model;
                CaptureReferenceScale();
            }

            public void CaptureReferenceScale()
            {
                ReferenceScale = Model.transform.localScale;
            }
        }
    }
}
