#if JULYGF_WX_MINIGAME
using July.Arch;
using WeChatWASM;

namespace July.Platform
{
    internal sealed class WeChatDirectPlayService : IWeChatDirectPlayService, ICanEvent
    {
        private bool _isListening;

        public bool IsDirectPlayLaunch { get; private set; }
        public bool HasEntered { get; private set; }

        public void Init()
        {
            if (!WX.CanIUse("GetDirectAdStatusSync")
                || !WX.CanIUse("OnDirectAdStatusChange"))
                return;

            WX.OnDirectAdStatusChange(OnStatusChanged);
            _isListening = true;

            var status = WX.GetDirectAdStatusSync();
            IsDirectPlayLaunch |= status.isInDirectGameAd || status.isInMask;
            if (status.isInDirectGameAd && !status.isInMask)
                ReportEntered();
        }

        private void OnStatusChanged(OnDirectAdStatusChangeListenerResult status)
        {
            IsDirectPlayLaunch |= status.isInDirectGameAd || status.isInMask;
            if (!IsDirectPlayLaunch) return;

            var pierced = status.isInDirectGameAd && !status.isInMask;
            var choseContinue = !status.isInDirectGameAd && !status.isInMask
                                && !status.isEndByAbnormal;
            if (pierced || choseContinue)
                ReportEntered();
        }

        private void ReportEntered()
        {
            HasEntered = true;
            this.Publish(new WeChatDirectPlayEnteredEvent());
        }

        public void Shutdown()
        {
            if (_isListening)
            {
                WX.OffDirectAdStatusChange(OnStatusChanged);
                _isListening = false;
            }
        }
    }
}
#endif
