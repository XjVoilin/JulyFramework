using UnityEngine;

namespace July.UI
{
    /// <summary>描述一个待显示的模型及其 UI 位置。</summary>
    public readonly struct ModelPreviewTarget
    {
        /// <summary>模型资源名称。</summary>
        public string ModelAssetName { get; }

        /// <summary>模型中心在 UI 中跟随的锚点。</summary>
        public RectTransform Anchor { get; }

        /// <summary>相对于模型资源原始缩放的显示倍率。</summary>
        public float DisplayScale { get; }

        public ModelPreviewTarget(
            string modelAssetName,
            RectTransform anchor,
            float displayScale = 1f)
        {
            ModelAssetName = modelAssetName;
            Anchor = anchor;
            DisplayScale = displayScale;
        }
    }
}
