using System;
using UnityEngine;

namespace July.UI
{
    /// <summary>描述一个待显示的模型。</summary>
    public readonly struct ModelPreviewTarget
    {
        /// <summary>模型资源名称。</summary>
        public string ModelAssetName { get; }

        /// <summary>模型缩放覆盖；未指定时使用预览实例的整体缩放。</summary>
        public float? ScaleOverride { get; }

        /// <summary>模型垂直偏移覆盖；未指定时使用预览实例的垂直偏移。</summary>
        public float? VerticalOffsetOverride { get; }

        /// <summary>Optional model instance configuration invoked after preview preparation.</summary>
        public Action<GameObject> ConfigureInstance { get; }

        public ModelPreviewTarget(
            string modelAssetName,
            Action<GameObject> configureInstance = null,
            float? scaleOverride = null,
            float? verticalOffsetOverride = null)
        {
            ModelAssetName = modelAssetName;
            ConfigureInstance = configureInstance;
            ScaleOverride = scaleOverride;
            VerticalOffsetOverride = verticalOffsetOverride;
        }
    }
}
