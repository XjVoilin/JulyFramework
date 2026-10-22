using System;
using UnityEngine;

namespace July.Platform
{
    [Serializable]
    public sealed class PlatformConfig
    {
        [InspectorName("启用 DPR 性能降档")]
        [Tooltip("按设备性能降低绘制 DPR；关闭时不应用性能倍率。启动时生效。")]
        public bool EnableDprPerformanceScaling = true;
        [InspectorName("启用绘制长边限制")]
        [Tooltip("根据最长边限制绘制 DPR；关闭时忽略最长边配置。启动时生效。")]
        public bool EnableFramebufferLongEdgeLimit = true;
        [Min(1)] public int MaxFramebufferLongEdge = 1600;
        public string WeChatRewardedAdUnitId = string.Empty;
        public string TikTokRewardedAdUnitId = string.Empty;
    }
}
