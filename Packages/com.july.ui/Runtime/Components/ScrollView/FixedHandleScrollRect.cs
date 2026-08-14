using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace July.UI
{
    /// <summary>
    /// 替代 ScrollRect，在 base.LateUpdate 之后强制固定 Scrollbar handle 尺寸。
    /// </summary>
    public class FixedHandleScrollRect : ScrollRect
    {
        [Header("Fixed Handle")]
        [FormerlySerializedAs("fixedHandleSize")]
        [SerializeField] private bool _fixedHandleSize;
        [FormerlySerializedAs("handleSizeRatio")]
        [SerializeField, Range(0.05f, 1f)] private float _handleSizeRatio = 0.15f;

        protected override void LateUpdate()
        {
            base.LateUpdate();

            if (!_fixedHandleSize) return;

            if (verticalScrollbar != null)
                verticalScrollbar.size = _handleSizeRatio;
            if (horizontalScrollbar != null)
                horizontalScrollbar.size = _handleSizeRatio;
        }
    }
}
