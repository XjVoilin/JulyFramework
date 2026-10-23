using System;
using UnityEngine;

namespace July.Platform
{
    [Serializable]
    public sealed class DprConfig
    {
        [InspectorName("启用分档像素预算")]
        [Tooltip("超出设备档位的整帧像素预算才降低 DPR；未知设备按中档。启动时生效。")]
        public bool EnablePixelBudget = true;
        [InspectorName("高档绘制像素预算")]
        [Tooltip("单位为像素总数，2100000 表示 210 万像素。仅启用分档像素预算时使用。")]
        [Min(1)] public int HighTierPixels = 2100000;
        [InspectorName("中档绘制像素预算")]
        [Tooltip("未知设备也使用此值。1500000 表示 150 万像素。仅启用分档像素预算时使用。")]
        [Min(1)] public int MediumTierPixels = 1500000;
        [InspectorName("低档绘制像素预算")]
        [Tooltip("1000000 表示 100 万像素。仅启用分档像素预算时使用。")]
        [Min(1)] public int LowTierPixels = 1000000;
    }
}
