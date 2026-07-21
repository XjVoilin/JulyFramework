using UnityEngine;

namespace July.Platform
{
    /// <summary>
    /// 分享结果事件（成�?失败�?
    /// </summary>
    public readonly struct ShareResultEvent
    {
        public readonly int ShareId;
        public readonly bool IsSuccess;
        public readonly string Message;

        public ShareResultEvent(int shareId, bool isSuccess, string message = null)
        {
            ShareId = shareId;
            IsSuccess = isSuccess;
            Message = message;
        }
    }

    public interface IShareService : IPlatformService
    {
        void Share(int shareId, string title, string imageUrl, string imgUrlId, string query);
        void ShowShareImageMenu(int shareId);
        void ShareCaptureArea(Rect rect, int shareId, string title, string query);
    }
}

