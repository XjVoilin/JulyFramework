using System;
using UnityEngine;

namespace July.Audio
{
    [Serializable]
    public struct AudioConfig
    {
        [Tooltip("默认按钮点击音效名（留空则不播放）")]
        public string DefaultClickSfx;

        public static AudioConfig Default => new()
        {
            DefaultClickSfx = "CommonBtnClick",
        };
    }
}
