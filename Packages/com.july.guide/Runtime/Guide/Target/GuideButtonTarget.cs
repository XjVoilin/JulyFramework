using System;
using UnityEngine;
using UnityEngine.UI;

namespace July.Guide
{
    /// <summary>Adapts an accepted UGUI Button action without intercepting pointer events.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class GuideButtonTarget : GuideUITarget, IGuideClickTarget
    {
        private Button _button;
        public event Action Clicked;

        protected override void OnViewAwake()
        {
            base.OnViewAwake();
            _button = GetComponent<Button>();
        }

        protected override void OnViewEnable()
        {
            _button.onClick.AddListener(ReportClick);
            try
            {
                // Registration can synchronously resume a waiting procedure.
                base.OnViewEnable();
            }
            catch
            {
                _button.onClick.RemoveListener(ReportClick);
                throw;
            }
        }

        protected override void OnViewDisable()
        {
            _button.onClick.RemoveListener(ReportClick);
            base.OnViewDisable();
        }

        // Button has already accepted this activation. Its other listener may have hidden it.
        private void ReportClick() => Clicked?.Invoke();
    }
}
