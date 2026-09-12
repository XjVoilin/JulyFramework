using System;
using UnityEngine;

namespace July.Platform
{
    [Serializable]
    public sealed class PlatformConfig
    {
        [Min(1)] public int MaxFramebufferLongEdge = 1600;
        public string WeChatRewardedAdUnitId = string.Empty;
        public string TikTokRewardedAdUnitId = string.Empty;
    }
}
