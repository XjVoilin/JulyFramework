using July.Arch;
#if JULYGF_WX_MINIGAME
using Cysharp.Threading.Tasks;
using UnityEngine;
using WeChatWASM;

namespace July.Platform
{
    public class WeChatShareService : IShareService, ICanEvent
    {
        private const float ShareSuccessThresholdSeconds = 2.5f;
        private const string MsgReturnedTooFast = "returned_too_fast";

        private int _pendingShareId;
        private float _shareStartTime;

        public void Init() { }

        public void DeferredInit()
        {
            WX.OnShow(_ => OnAppShow());
        }

        public void Share(int shareId, string title, string imageUrl, string templateId, string query)
        {
            Debug.Log($"[WeChatShare] Share shareId={shareId}, title={title}, imageUrl={imageUrl}, templateId={templateId}, query={query}");

            if (_pendingShareId != 0)
                Debug.LogWarning($"[WeChatShare] 覆盖未结算的分享 pending shareId={_pendingShareId}，前一次将被丢弃。");

            var opt = new ShareAppMessageOption
            {
                title = title,
                query = query
            };

            if (!string.IsNullOrEmpty(imageUrl))
                opt.imageUrl = imageUrl;
            if (!string.IsNullOrEmpty(templateId))
                opt.imageUrlId = templateId;

            _pendingShareId = shareId;
            _shareStartTime = Time.realtimeSinceStartup;
            WX.ShareAppMessage(opt);
        }

        public void ShowShareImageMenu(int shareId)
        {
            WaitAndCapture().Forget();
        }

        public void ShareCaptureArea(Rect rect, int shareId, string title, string query)
        {
            WaitAndCaptureArea(rect, shareId, title, query).Forget();
        }

        private void OnAppShow()
        {
            if (_pendingShareId == 0) return;

            var elapsed = Time.realtimeSinceStartup - _shareStartTime;
            var isSuccess = elapsed >= ShareSuccessThresholdSeconds;
            var shareId = _pendingShareId;

            _pendingShareId = 0;
            _shareStartTime = 0f;

            Debug.Log($"[WeChatShare] OnAppShow shareId={shareId}, elapsed={elapsed:F2}s, isSuccess={isSuccess}");
            this.Publish(new ShareResultEvent(
                shareId,
                isSuccess,
                isSuccess ? null : MsgReturnedTooFast));
        }

        private async UniTask WaitAndCapture()
        {
            await UniTask.DelayFrame(2);
        }

        private async UniTask WaitAndCaptureArea(Rect rect, int shareId, string title, string query)
        {
            await UniTask.DelayFrame(2);
        }
    }
}
#endif

