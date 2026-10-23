using System;
using UnityEngine;

namespace July.Platform
{
    [Serializable]
    public sealed class PlatformConfig
    {
        [InspectorName("绘制分辨率")]
        public DprConfig Dpr = new DprConfig();
        public string WeChatRewardedAdUnitId = string.Empty;
        public string TikTokRewardedAdUnitId = string.Empty;
    }
}
