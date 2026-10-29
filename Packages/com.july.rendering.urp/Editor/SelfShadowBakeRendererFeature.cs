using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace July.Rendering.URP.Editor
{
    // Exists only on the temporary renderer used by SelfShadowBakeWindow.
    public sealed class SelfShadowBakeRendererFeature : ScriptableRendererFeature
    {
        internal Mesh Mesh;
        internal Matrix4x4 Matrix;
        internal Material Material;
        internal RenderTexture Minimum, Maximum;
        BakePass _pass;

        public override void Create() => _pass = new BakePass(this) { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) => renderer.EnqueuePass(_pass);

        sealed class BakePass : ScriptableRenderPass
        {
            readonly SelfShadowBakeRendererFeature _owner;
            internal BakePass(SelfShadowBakeRendererFeature owner) => _owner = owner;
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var cmd = CommandBufferPool.Get("July Self Shadow UV Bake");
                cmd.SetRenderTarget(_owner.Minimum);
                cmd.ClearRenderTarget(false, true, Color.white);
                cmd.DrawMesh(_owner.Mesh, _owner.Matrix, _owner.Material, 0, 0);
                cmd.SetRenderTarget(_owner.Maximum);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.DrawMesh(_owner.Mesh, _owner.Matrix, _owner.Material, 0, 1);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
