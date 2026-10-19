using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace July.Rendering.URP
{
    /// <summary>Draws one camera's mesh highlight using the existing color/depth/stencil target.</summary>
    public sealed class MeshHighlightRendererFeature : ScriptableRendererFeature
    {
        private HighlightPass _pass;

        public override void Create()
        {
            _pass = new HighlightPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (!camera.TryGetComponent<MeshHighlightCamera>(out var source) || !source.isActiveAndEnabled) return;
            if (source.Target == null || !source.Target.IsHighlighted) return;
            renderer.EnqueuePass(_pass);
        }

        private sealed class HighlightPass : ScriptableRenderPass
        {
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var camera = renderingData.cameraData.camera;
                var source = camera.GetComponent<MeshHighlightCamera>();
                var commands = CommandBufferPool.Get("July mesh highlight");
                source.Target.RecordHighlight(commands, camera.aspect);
                context.ExecuteCommandBuffer(commands);
                CommandBufferPool.Release(commands);
            }
        }
    }
}
