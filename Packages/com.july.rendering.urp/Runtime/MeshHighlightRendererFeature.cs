using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace July.Rendering.URP
{
    public sealed class MeshHighlightRendererFeature : ScriptableRendererFeature
    {
        MaskPass _maskPass;
        CompositePass _compositePass;
        public static readonly ProfilingSampler MaskSampler = new ProfilingSampler("Mask highlight: mask");
        public static readonly ProfilingSampler CompositeSampler = new ProfilingSampler("Mask highlight: composite");
        internal RTHandle Mask => _maskPass.Texture;
        public RenderTexture DiagnosticMask => _maskPass?.Texture?.rt;
        internal Rect ScreenRect => _maskPass.ScreenRect;

        public override void Create()
        {
            _maskPass?.Dispose();
            _maskPass = new MaskPass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
            _compositePass = new CompositePass(_maskPass) { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (data.cameraData.renderType != CameraRenderType.Base) return;
            var camera = data.cameraData.camera;
            if (!camera.TryGetComponent<MeshHighlightCamera>(out var source) || !source.isActiveAndEnabled ||
                source.Target == null || !source.Target.IsHighlighted) return;
            var desc = data.cameraData.cameraTargetDescriptor;
            if (!camera.orthographic || desc.msaaSamples != 1)
                throw new InvalidOperationException("Mask highlight trial requires an orthographic camera without MSAA.");
            if (!source.Target.GetScreenRect(camera, desc.width, desc.height, out var rect)) return;
            _maskPass.Target = source.Target; _maskPass.ScreenRect = rect;
            renderer.EnqueuePass(_maskPass);
            renderer.EnqueuePass(_compositePass);
        }

        protected override void Dispose(bool disposing) => _maskPass?.Dispose();
        void OnDisable() => _maskPass?.Dispose();

        sealed class MaskPass : ScriptableRenderPass
        {
            internal RTHandle Texture;
            internal MeshHighlightTarget Target;
            internal Rect ScreenRect;
            internal void Dispose() { Texture?.Release(); Texture = null; }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
            {
                if (!SystemInfo.IsFormatSupported(GraphicsFormat.R8G8_UNorm, FormatUsage.Render))
                    throw new NotSupportedException("RG8 color attachment is required by mask highlight.");
                var desc = data.cameraData.cameraTargetDescriptor;
                desc.graphicsFormat = GraphicsFormat.R8G8_UNorm;
                desc.depthBufferBits = 0; desc.depthStencilFormat = GraphicsFormat.None;
                desc.msaaSamples = 1; desc.useMipMap = false; desc.autoGenerateMips = false;
                RenderingUtils.ReAllocateIfNeeded(ref Texture, desc, FilterMode.Point,
                    TextureWrapMode.Clamp, name: "_JulyMaskHighlight");
                var depth = data.cameraData.renderer.cameraDepthTargetHandle;
                // URP 14 binds a pass's own color target as depth when given CameraTarget.
                // This feature needs the addressable depth provided by the project's camera stack.
                if (depth.nameID == BuiltinRenderTextureType.CameraTarget)
                    throw new InvalidOperationException("Mask highlight requires an addressable camera depth attachment (camera stack or intermediate target).");
                ConfigureTarget(Texture, depth);
                ConfigureClear(ClearFlag.Color, Color.clear);
                ConfigureColorStoreAction(RenderBufferStoreAction.Store);
                ConfigureDepthStoreAction(RenderBufferStoreAction.Store);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var cmd = CommandBufferPool.Get("Mask highlight: mask");
                using (new ProfilingScope(cmd, MaskSampler))
                {
                    cmd.EnableScissorRect(ScreenRect);
                    Target.RecordMask(cmd, 0); Target.RecordMask(cmd, 1);
                    cmd.DisableScissorRect();
                }
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }

        sealed class CompositePass : ScriptableRenderPass
        {
            readonly MaskPass _mask;
            readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
            static readonly int MaskId = Shader.PropertyToID("_HighlightMask");
            static readonly int SizeId = Shader.PropertyToID("_HighlightMaskSize");
            static readonly int WidthId = Shader.PropertyToID("_HighlightWidthFactors");
            public CompositePass(MaskPass mask) => _mask = mask;

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
            {
                ConfigureTarget(data.cameraData.renderer.cameraColorTargetHandle,
                    data.cameraData.renderer.cameraDepthTargetHandle);
                ConfigureClear(ClearFlag.None, Color.clear);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var cmd = CommandBufferPool.Get("Mask highlight: composite");
                using (new ProfilingScope(cmd, CompositeSampler))
                {
                    var desc = data.cameraData.cameraTargetDescriptor;
                    _properties.SetTexture(MaskId, _mask.Texture.rt);
                    _properties.SetVector(SizeId, new Vector4(1f / desc.width, 1f / desc.height, desc.width, desc.height));
                    _properties.SetVector(WidthId, new Vector4(desc.height / (100f * data.cameraData.camera.orthographicSize),
                        desc.width * .0070711f, desc.width * .01f, 0));
                    cmd.EnableScissorRect(_mask.ScreenRect);
                    cmd.DrawProcedural(Matrix4x4.identity, _mask.Target.Material, 2, MeshTopology.Triangles, 3, 1, _properties);
                    cmd.DisableScissorRect();
                }
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
        }
    }
}
