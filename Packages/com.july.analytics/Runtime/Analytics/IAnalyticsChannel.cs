using System.Collections.Generic;

namespace July.Analytics
{
    /// <summary>单个统计 SDK 的最小适配边界。</summary>
    public interface IAnalyticsChannel
    {
        void Initialize();
        void Track(string eventName, Dictionary<string, object> parameters);
        void SetUserId(string userId);
        void SetUserProperties(Dictionary<string, object> properties);
        void Flush();
        void SetLogEnabled(bool enabled);
        void Shutdown();
    }
}
