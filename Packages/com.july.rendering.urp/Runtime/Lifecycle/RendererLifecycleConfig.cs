using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace July.Rendering.URP.Lifecycle
{
    [Serializable]
    public struct RendererLifecycleConfig
    {
        public UniversalRenderPipelineAsset Pipeline;

        [Tooltip("允许闲置回收的 Renderer 列表；每项索引必须与管线 Renderer List 中的配置资源对应。")]
        public RendererEntry[] Renderers;

        [Serializable]
        public struct RendererEntry
        {
            [Tooltip("管线 Renderer List 中从 0 开始的明确索引。")]
            public int RendererIndex;

            [Tooltip("该索引对应的 Renderer 配置资源，必须支持释放后重新创建。")]
            public ScriptableRendererData RendererData;
        }
    }
}
