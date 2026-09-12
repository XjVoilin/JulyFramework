using System;
using July.Arch;
using July.Release;

namespace July.Bootstrap
{
    /// <summary>保存成功启动产生的信息，供资源初始化和业务代码共享。</summary>
    public sealed class LaunchInfoStore : StoreBase
    {
        private LaunchInfo _current;

        /// <summary>FetchConfigStep 成功后才可读取；提前读取违反启动顺序约定。</summary>
        public LaunchInfo Current => _current ?? throw new InvalidOperationException(
            "Launch information is unavailable before the configuration step succeeds.");

        // Store 只注册一次；重试成功后整体替换结果。
        internal void SetResult(LaunchInfo result) => _current = result;
    }

    /// <summary>一次成功配置请求产生的不可变结果，不持有运行中的服务实例。</summary>
    public sealed class LaunchInfo
    {
        public ReleaseEnvironment Env { get; }
        public bool IsDev => Env == ReleaseEnvironment.Dev;
        public string ServerUrl { get; }
        public string PlanVersion { get; }
        public bool IsAudit { get; }
        public string RemoteUrl { get; }

        internal LaunchInfo(ReleaseEnvironment env, string serverUrl, string planVersion,
            bool isAudit, string remoteUrl)
        {
            Env = env;
            ServerUrl = serverUrl;
            PlanVersion = planVersion;
            IsAudit = isAudit;
            RemoteUrl = remoteUrl;
        }
    }
}