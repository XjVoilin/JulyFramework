using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace July.Rendering.URP.Lifecycle
{
    [Serializable]
    public struct IdleRendererReclaimConfig
    {
        public UniversalRenderPipelineAsset Pipeline;

        [Tooltip("管线 Renderer 列表中的明确索引，仅回收此索引对应的实例。")]
        public int RendererIndex;

        [Tooltip("必须对应管线中指定索引的 Renderer2DData，回收时由 URP 销毁旧实例并重建。")]
        public Renderer2DData RendererData;
    }
}
