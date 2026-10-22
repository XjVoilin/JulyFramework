using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace July.Rendering.URP.Lifecycle
{
    /// <summary>
    /// 显示交接后，分别回收明确配置且已闲置的 Renderer。
    /// 相机必须通过 URP 获取当前实例，不得跨回收保留旧 Renderer 或 Pass 引用。
    /// 禁用相机的手动绘制在执行期间受到保护，后续绘制通过 URP 使用替换后的实例。
    /// </summary>
    public sealed class IdleRendererReclaimer : IDisposable
    {
        private readonly UniversalRenderPipelineAsset _pipeline;
        private readonly RendererState[] _renderers;

        private sealed class RendererState
        {
            public readonly int Index;
            public readonly ScriptableRendererData Data;
            public bool Pending;
            public bool HasRendered;
            public bool RenderedThisFrame;

            public RendererState(RendererLifecycleConfig.RendererEntry entry)
            {
                Index = entry.RendererIndex;
                Data = entry.RendererData;
            }
        }

        public IdleRendererReclaimer(RendererLifecycleConfig config)
        {
            if (config.Pipeline == null || config.Renderers == null || config.Renderers.Length == 0)
                throw new ArgumentException("闲置 Renderer 回收需要管线和非空的 Renderer 配置列表。", nameof(config));

            _pipeline = config.Pipeline;
            _renderers = new RendererState[config.Renderers.Length];
            for (var i = 0; i < config.Renderers.Length; i++)
            {
                var entry = config.Renderers[i];
                if (entry.RendererIndex < 0)
                    throw new ArgumentOutOfRangeException(nameof(config), $"Renderer 配置第 {i} 项必须指定非负索引。");
                if (entry.RendererData == null)
                    throw new ArgumentException($"Renderer 配置第 {i} 项缺少 RendererData。", nameof(config));
                for (var previous = 0; previous < i; previous++)
                    if (_renderers[previous].Index == entry.RendererIndex || _renderers[previous].Data == entry.RendererData)
                        throw new ArgumentException($"Renderer 配置第 {i} 项重复指定索引或 RendererData。", nameof(config));
                _renderers[i] = new RendererState(entry);
            }

            RenderPipelineManager.beginCameraRendering += OnCameraRendering;
            RenderPipelineManager.endFrameRendering += OnFrameRendered;
        }

        public void RequestReclaim()
        {
            foreach (var renderer in _renderers)
                renderer.Pending = true;
        }

        private bool IsCurrentPipeline => GraphicsSettings.currentRenderPipeline == _pipeline;

        private void OnCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!IsCurrentPipeline) return;
            var selected = SelectedRenderer(camera);
            foreach (var renderer in _renderers)
            {
                if (!ReferenceEquals(selected, _pipeline.GetRenderer(renderer.Index))) continue;
                renderer.HasRendered = true;
                renderer.RenderedThisFrame = true;
            }
        }

        private void OnFrameRendered(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (var renderer in _renderers)
            {
                var rendered = renderer.RenderedThisFrame;
                renderer.RenderedThisFrame = false;
                if (!renderer.Pending || !IsCurrentPipeline) continue;
                // 新实例尚未绘制，没有可回收的相机附件，独立消费该项请求。
                if (!renderer.HasRendered)
                {
                    renderer.Pending = false;
                    continue;
                }
                if (rendered) continue;

                var previous = _pipeline.GetRenderer(renderer.Index);
                if (HasActiveCamera(previous)) continue;
                Recreate(renderer, previous);
            }
        }

        private bool HasActiveCamera(ScriptableRenderer renderer)
        {
            foreach (var camera in Camera.allCameras)
            {
                if (ReferenceEquals(SelectedRenderer(camera), renderer)) return true;
                if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) ||
                    data.renderType != CameraRenderType.Base) continue;
                var stack = data.cameraStack;
                if (stack == null) continue;
                foreach (var overlay in stack)
                    if (overlay != null && overlay.isActiveAndEnabled &&
                        ReferenceEquals(SelectedRenderer(overlay), renderer)) return true;
            }
            return false;
        }

        private void Recreate(RendererState renderer, ScriptableRenderer previous)
        {
            // 实例销毁和缓存替换由 URP 负责，直接 Dispose 会让缓存继续指向已释放的实例。
            // 重建前先消费该项请求，避免配置错误导致每帧重复执行。
            renderer.Pending = false;
            renderer.Data.SetDirty();
            var replacement = _pipeline.GetRenderer(renderer.Index);
            if (ReferenceEquals(previous, replacement))
                throw new InvalidOperationException($"Renderer 索引 {renderer.Index} 未发生实例替换，请检查 RendererData 是否与管线索引对应。");
            renderer.HasRendered = false;
        }

        private ScriptableRenderer SelectedRenderer(Camera camera)
        {
            return camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)
                ? data.scriptableRenderer : _pipeline.scriptableRenderer;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnCameraRendering;
            RenderPipelineManager.endFrameRendering -= OnFrameRendered;
            foreach (var renderer in _renderers)
                renderer.Pending = false;
            // Renderer 实例仍归管线所有，关闭回收模块时不释放它们。
        }
    }
}
