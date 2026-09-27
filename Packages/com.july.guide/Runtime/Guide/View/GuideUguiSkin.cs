using System;
using UnityEngine;
using UnityEngine.UI;

namespace July.Guide
{
    /// <summary>
    /// UGUI-only appearance seam. July UI owns the window instance; GuidePresentation owns interaction subscriptions.
    /// RaycastSurface must cover the screen; decorative graphics must not intercept raycasts.
    /// </summary>
    public abstract class GuideUguiSkin : MonoBehaviour
    {
        public event Action Confirmed;
        public event Action SkipRequested;
        public abstract Graphic RaycastSurface { get; }

        public abstract void Configure(GuideViewData data, string message, string skipLabel, bool canSkip);
        public abstract void SetTargetRect(Rect? targetRect);
        public abstract void ShowConfirmation(bool visible, bool interactable, string label = null);

        protected void ReportConfirmation() => Confirmed?.Invoke();
        protected void ReportSkip() => SkipRequested?.Invoke();
    }
}
