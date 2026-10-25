using July.Logging;
using July.Arch;
#if JULYGF_DY_MINIGAME
using UnityEngine;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;

namespace July.Platform
{
    public class TikTokShareService : IShareService, ICanEvent
    {
        public void Share(int shareId, string title, string imageUrl, string templateId, string query)
        {
            JLogger.Log($"[TikTokShare] Share shareId={shareId}, title={title}, imageUrl={imageUrl}, templateId={templateId}, query={query}");

            var shareJson = new JsonData();
            shareJson["title"] = title;
            shareJson["query"] = query;

            if (!string.IsNullOrEmpty(templateId))
                shareJson["templateId"] = templateId;

            TT.ShareAppMessage(shareJson,
                _ =>
                {
                    JLogger.Log($"[TikTokShare] Success shareId={shareId}");
                    this.Publish(new ShareResultEvent(shareId, true));
                },
                err =>
                {
                    JLogger.LogWarning($"[TikTokShare] Fail shareId={shareId}, err={err}");
                    this.Publish(new ShareResultEvent(shareId, false, err));
                },
                () =>
                {
                    JLogger.Log($"[TikTokShare] Cancel shareId={shareId}");
                    this.Publish(new ShareResultEvent(shareId, false, "cancel"));
                });
        }

        public void ShowShareImageMenu(int shareId)
        {
            ShareCaptureArea(new Rect(0, 0, Screen.width, Screen.height), shareId, "", "");
        }

        public void ShareCaptureArea(Rect rect, int shareId, string title, string query)
        {
            Share(shareId, title, "", "", query);
        }
    }
}
#endif

