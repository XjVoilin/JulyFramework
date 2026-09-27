using System;
using UnityEngine;

namespace July.Guide
{
    public interface IGuideTarget
    {
        int TargetId { get; }
        Rect ScreenRect { get; }
    }

    /// <summary>A UI interaction accepted by this target, not a gameplay completion signal.</summary>
    public interface IGuideClickTarget : IGuideTarget
    {
        event Action Clicked;
    }
}
