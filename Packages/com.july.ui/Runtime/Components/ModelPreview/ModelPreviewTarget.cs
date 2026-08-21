using System;
using UnityEngine;

namespace July.UI
{
    /// <summary>描述一个待显示的模型。</summary>
    public readonly struct ModelPreviewTarget
    {
        /// <summary>模型资源名称。</summary>
        public string ModelAssetName { get; }

        /// <summary>Optional model instance configuration invoked after preview preparation.</summary>
        public Action<GameObject> ConfigureInstance { get; }

        public ModelPreviewTarget(
            string modelAssetName,
            Action<GameObject> configureInstance = null)
        {
            ModelAssetName = modelAssetName;
            ConfigureInstance = configureInstance;
        }
    }
}
