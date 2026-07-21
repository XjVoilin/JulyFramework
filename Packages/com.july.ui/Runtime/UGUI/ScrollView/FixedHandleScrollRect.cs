using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>
    /// 替代 ScrollRect，在 base.LateUpdate 之后强制固定 Scrollbar handle 尺寸。
    /// </summary>
    public class FixedHandleScrollRect : ScrollRect
    {
        [Header("Fixed Handle")]
        [SerializeField] private bool fixedHandleSize;
        [SerializeField, Range(0.05f, 1f)] private float handleSizeRatio = 0.15f;

        protected override void LateUpdate()
        {
            base.LateUpdate();

            if (!fixedHandleSize) return;

            if (verticalScrollbar != null)
                verticalScrollbar.size = handleSizeRatio;
            if (horizontalScrollbar != null)
                horizontalScrollbar.size = handleSizeRatio;
        }
    }
}
