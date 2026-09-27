using System;
using July.UI;
using UnityEngine;

namespace July.Guide
{
    /// <summary>July UI window hosting a replaceable guide skin. It never advances a guide.</summary>
    public sealed class GuideWindow : UIView
    {
        [SerializeField] private GuideUguiSkin _skin;
        internal GuideUguiSkin Skin => _skin;
        internal Action Refresh;
        internal Action Closing;

        protected override void OnBeforeOpen()
        {
            var owner = GetData<GuidePresentation>();
            if (owner == null)
                throw new InvalidOperationException("Open GuideWindow through GuidePresentation.");
            owner.AttachWindow(this);
        }

        protected override void OnOpen() => Refresh?.Invoke();
        private void LateUpdate() => Refresh?.Invoke();
        protected override void OnClose() => NotifyClosing();
        protected override void OnAfterClose() => NotifyClosing();

        private void NotifyClosing()
        {
            Refresh = null;
            var notify = Closing;
            Closing = null;
            notify?.Invoke();
        }
    }
}
