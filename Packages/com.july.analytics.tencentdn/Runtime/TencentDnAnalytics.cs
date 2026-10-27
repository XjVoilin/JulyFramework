using System.Runtime.InteropServices;
using July.Logging;
using LitJson;

namespace July.Analytics.TencentDn
{
    /// <summary>腾讯独立发送入口；项目负责配置和业务触发，SDK 实例由 JS 初始化拥有。</summary>
    public static class TencentDnAnalytics
    {
        private static bool _enabled;

        /// <summary>启动时传入项目配置，只检查 JS 就绪状态，不在 C# 中创建 SDK。</summary>
        public static void Configure(TencentDnConfig config)
        {
            _enabled = false;
#if JULYGF_WX_MINIGAME && !UNITY_EDITOR
            if (!config.Enabled) return;
            _enabled = IsReadyNative();
            if (!_enabled)
                JLogger.LogError("[TencentDn] 配置已启用，但 JS SDK 未成功初始化，本次腾讯上报已停用。请检查完整导出产物。");
#endif
        }

        /// <summary>返回 SDK 是否接受事件，不代表远端已接收；不会自动转发到数数。</summary>
        public static bool Track(IBIEvent evt)
        {
            if (!_enabled) return false;
#if JULYGF_WX_MINIGAME && !UNITY_EDITOR
            var code = TrackNative(evt.EventName, JsonMapper.ToJson(evt.ToParams()));
            if (code == 0) return true;
            JLogger.LogError($"[TencentDn] SDK 拒绝事件 {evt.EventName}，code={code}");
#endif
            return false;
        }

        /// <summary>项目取得真实微信 OpenID 后主动调用，不使用项目账号 ID 替代。</summary>
        public static bool SetOpenId(string openId)
        {
            if (!_enabled) return false;
#if JULYGF_WX_MINIGAME && !UNITY_EDITOR
            var code = SetOpenIdNative(openId);
            if (code == 0) return true;
            JLogger.LogError($"[TencentDn] SDK 拒绝绑定 OpenID，code={code}");
#endif
            return false;
        }

#if JULYGF_WX_MINIGAME && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "July_TencentDn_IsReady")]
        private static extern bool IsReadyNative();

        [DllImport("__Internal", EntryPoint = "July_TencentDn_Track")]
        private static extern int TrackNative(string eventName, string parameters);

        [DllImport("__Internal", EntryPoint = "July_TencentDn_SetOpenId")]
        private static extern int SetOpenIdNative(string openId);
#endif
    }
}
