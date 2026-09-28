using System;
using UnityEngine;

namespace July.Input
{
    /// <summary>距离按屏幕短边换算到参考像素；System 创建时读取一次。</summary>
    [Serializable]
    public sealed class InputConfig
    {
        public bool EnableClick = true;
        public bool EnableSwipe = true;
        public bool EnableKeyboard = true;
        [Min(1f)] public float ReferenceShortSide = 1080f;
        [Min(0f)] public float ClickTolerance = 20f;
        [Min(1f)] public float SwipeDistance = 80f;
        public KeyCode Up = KeyCode.UpArrow;
        public KeyCode Down = KeyCode.DownArrow;
        public KeyCode Left = KeyCode.LeftArrow;
        public KeyCode Right = KeyCode.RightArrow;

        internal void Validate()
        {
            if (!(ReferenceShortSide > 0f) || float.IsInfinity(ReferenceShortSide) ||
                !(ClickTolerance >= 0f) || float.IsInfinity(ClickTolerance) ||
                !(SwipeDistance > ClickTolerance) || float.IsInfinity(SwipeDistance))
                throw new ArgumentException("输入参考尺寸须为正数，点击容差须非负，滑动距离须大于点击容差，且均为有限值。");
        }
    }
}
