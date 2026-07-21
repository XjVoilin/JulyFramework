using July.Arch;
using UnityEngine;

namespace July.Platform
{
    public class DefaultShareService : IShareService, ICanEvent
    {
        public void Share(int shareId, string title, string imageUrl, string imgUrlId, string query)
        {
            Debug.Log($"[DefaultShare] Share shareId={shareId} (Editor stub, auto-success)");
            this.Publish(new ShareResultEvent(shareId, true));
        }

        public void ShowShareImageMenu(int shareId)
        {
            Debug.Log($"[DefaultShare] ShowShareImageMenu shareId={shareId} (Editor stub, auto-success)");
            this.Publish(new ShareResultEvent(shareId, true));
        }

        public void ShareCaptureArea(Rect rect, int shareId, string title, string query)
        {
            Debug.Log($"[DefaultShare] ShareCaptureArea shareId={shareId} (Editor stub, auto-success)");
            this.Publish(new ShareResultEvent(shareId, true));
        }
    }
}

