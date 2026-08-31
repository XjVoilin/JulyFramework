using UnityEngine;

namespace July.Guide
{
    public interface IGuideTarget
    {
        int TargetId { get; }
        Rect ScreenRect { get; }
    }
}
