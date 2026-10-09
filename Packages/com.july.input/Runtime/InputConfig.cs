using System;
using UnityEngine;

namespace July.Input
{
    /// <summary>可选便捷识别的配置；关闭全部识别不影响基础指针采集。System 创建时读取一次。</summary>
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
            if ((EnableClick || EnableSwipe) &&
                (!(ReferenceShortSide > 0f) || float.IsInfinity(ReferenceShortSide)))
                throw new ArgumentException("手势参考尺寸必须是有限正数。");
            if (EnableClick && (!(ClickTolerance >= 0f) || float.IsInfinity(ClickTolerance)))
                throw new ArgumentException("点击容差必须是有限非负数。");
            if (EnableSwipe && (!(SwipeDistance > 0f) || float.IsInfinity(SwipeDistance)))
                throw new ArgumentException("滑动距离必须是有限正数。");
            if (EnableClick && EnableSwipe && SwipeDistance <= ClickTolerance)
                throw new ArgumentException("同时启用点击和滑动时，滑动距离必须大于点击容差。");
        }
    }
}
