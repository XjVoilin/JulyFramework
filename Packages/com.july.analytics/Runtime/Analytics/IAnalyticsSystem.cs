using System.Collections.Generic;

namespace July.Analytics
{
    /// <summary>
    /// 数据统计系统接口 — 事件上报、用户属性、开关控制。
    /// 通过 Scope.GetSystem&lt;IAnalyticsSystem&gt;() 获取。
    /// </summary>
    public interface IAnalyticsSystem
    {
        bool IsEnabled { get; }
        void SetEnabled(bool enabled);
        void Track(string eventName, Dictionary<string, object> parameters = null);
        void Track<T>(T evt) where T : struct, IBIEvent;
        void SetUserId(string userId);
        void SetUserProperties(Dictionary<string, object> properties);
        void SetUserProperties<T>(T props) where T : struct, IBIProperties;
        void Flush();
        void SetLogEnabled(bool enabled);
    }
}
