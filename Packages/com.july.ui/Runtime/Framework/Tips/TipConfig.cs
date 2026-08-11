using System;
using UnityEngine;

namespace July.UI
{
    [Serializable]
    public struct TipConfig
    {
        [Tooltip("Tip 预制体资源路径")]
        public string TipPrefabPath;

        [Tooltip("Tip 对象池最大数量")]
        public int PoolMaxSize;

        [Tooltip("Tip 默认显示时长（秒）")]
        public float DefaultDuration;

        [Tooltip("Tip 淡出时长（秒）")]
        public float FadeOutDuration;

        [Tooltip("Tip 之间的间距")]
        public float Spacing;

        [Tooltip("Tip 上移动画时长")]
        public float MoveUpDuration;

        [Tooltip("Tip 入场动画起始偏移（从下方多少像素滑入）")]
        public float EnterOffset;

        [Tooltip("Tip 入场动画时长（秒）")]
        public float EnterDuration;

        public static TipConfig Default => new()
        {
            TipPrefabPath = "UITipItem",
            PoolMaxSize = 10,
            DefaultDuration = 2f,
            FadeOutDuration = 0.3f,
            Spacing = 10f,
            MoveUpDuration = 0.2f,
            EnterOffset = 50f,
            EnterDuration = 0.2f,
        };
    }
}
