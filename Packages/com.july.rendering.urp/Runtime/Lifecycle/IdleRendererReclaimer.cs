using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace July.Rendering.URP.Lifecycle
{
    /// <summary>
    /// 显示交接后，回收一个明确配置且已闲置的 2D Renderer。
    /// 相机必须通过 URP 获取当前实例，不得跨回收保留旧 Renderer 或 Pass 引用。
    /// 禁用相机的手动绘制在执行期间受到保护，后续绘制通过 URP 使用替换后的实例。
    /// </summary>
    public sealed class IdleRendererReclaimer : IDisposable
    {
        private readonly UniversalRenderPipelineAsset _pipeline;
        private readonly Renderer2DData _rendererData;
        private readonly int _rendererIndex;
        private bool _pending;
        private bool _hasRendered;
        private bool _renderedThisFrame;

        public IdleRendererReclaimer(IdleRendererReclaimConfig config)
        {
            if (config.Pipeline == null || config.RendererData == null)
                throw new ArgumentException("Idle renderer reclamation requires a pipeline and its Renderer2DData.", nameof(config));
            if (config.RendererIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(config), "An explicit non-negative renderer index is required.");
            _pipeline = config.Pipeline;
            _rendererData = config.RendererData;
            _rendererIndex = config.RendererIndex;
            RenderPipelineManager.beginCameraRendering += OnCameraRendering;
            RenderPipelineManager.endFrameRendering += OnFrameRendered;
        }

        public void RequestReclaim() => _pending = true;

        private bool IsCurrentPipeline => GraphicsSettings.currentRenderPipeline == _pipeline;

        private void OnCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!IsCurrentPipeline) return;
            if (!UsesRenderer(camera, _pipeline.GetRenderer(_rendererIndex))) return;
            _hasRendered = true;
            _renderedThisFrame = true;
        }

        private void OnFrameRendered(ScriptableRenderContext context, Camera[] cameras)
        {
            var rendered = _renderedThisFrame;
            _renderedThisFrame = false;
            if (!_pending || !IsCurrentPipeline) return;
            // 尚未绘制的新实例没有可回收的相机附件，无需再次重建。
            if (!_hasRendered)
            {
                _pending = false;
                return;
            }
            if (rendered) return;

            var previous = _pipeline.GetRenderer(_rendererIndex);
            foreach (var camera in Camera.allCameras)
            {
                if (UsesRenderer(camera, previous)) return;
                if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) ||
                    data.renderType != CameraRenderType.Base) continue;
                var stack = data.cameraStack;
                if (stack == null) continue;
                foreach (var overlay in stack)
                    if (overlay != null && overlay.isActiveAndEnabled && UsesRenderer(overlay, previous)) return;
            }

            // 实例销毁和缓存替换由 URP 负责，直接 Dispose 会让缓存继续指向已释放的实例。
            // 重建前先消费请求，避免配置错误导致每帧重复执行。
            _pending = false;
            _rendererData.SetDirty();
            var replacement = _pipeline.GetRenderer(_rendererIndex);
            if (ReferenceEquals(previous, replacement))
                throw new InvalidOperationException("Idle renderer RendererData does not match the configured pipeline index.");
            _hasRendered = false;
        }

        private bool UsesRenderer(Camera camera, ScriptableRenderer renderer)
        {
            var selected = camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)
                ? data.scriptableRenderer : _pipeline.scriptableRenderer;
            return ReferenceEquals(selected, renderer);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnCameraRendering;
            RenderPipelineManager.endFrameRendering -= OnFrameRendered;
            _pending = false;
            // Renderer 实例仍归管线所有，关闭回收模块时不释放它们。
        }
    }
}
