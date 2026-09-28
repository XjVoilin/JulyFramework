using System;
using UnityEngine;

namespace July.Audio
{
    [Serializable]
    public struct AudioConfig
    {
        [Tooltip("默认按钮点击音效名（留空则不播放）")]
        public string DefaultClickSfx;

        [Min(1)]
        [Tooltip("同名音效同时存在的实例上限（含延迟播放）；满额时停止最早登记的实例，播放新音效")]
        public int MaxInstancesPerSfx;

        public static AudioConfig Default => new()
        {
            DefaultClickSfx = "CommonBtnClick",
            MaxInstancesPerSfx = 4,
        };
    }
}
