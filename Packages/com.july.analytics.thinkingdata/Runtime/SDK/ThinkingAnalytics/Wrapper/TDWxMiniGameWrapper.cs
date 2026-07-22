#if TD_WEIXIN_GAME_MODE || MINI_GAME_WECHAT
using System.Runtime.InteropServices;
namespace ThinkingData.Analytics.Wrapper
{
    public  class TDWxMiniGameWrapper
    {
        [DllImport("__Internal")]
        public static extern void SetOpenId(string openid);

        [DllImport("__Internal")]
        public static extern void SetUnionId(string unionid);

        [DllImport("__Internal")]
        public static extern void OnTrack(string type,string p);

        [DllImport("__Internal")]
        public static extern bool IsWxPlatform();
    }
}
#endif