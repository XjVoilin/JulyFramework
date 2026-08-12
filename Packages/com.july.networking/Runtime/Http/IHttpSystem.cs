using System.Threading;
using Cysharp.Threading.Tasks;

namespace July.Networking
{
    /// <summary>
    /// HTTP 系统接口 — 直发请求、队列请求、默认头管理、离线队列重放。
    /// 通过 ArchContext.GetSystem&lt;IHttpSystem&gt;() 获取。
    /// </summary>
    public interface IHttpSystem
    {
        void Configure(HttpModuleOptions options, IHttpHandler handler);
        void SetDefaultHeader(string key, string value);
        void RemoveDefaultHeader(string key);
        UniTask SendAsync(HttpEntity entity, CancellationToken ct = default);
        void Send(HttpQueueEntity entity);
        bool HasPendingEntries();
        UniTask ReplayPendingAsync();
    }
}
