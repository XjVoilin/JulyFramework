using System;
using July.Arch;
using July.Release;
using UnityEngine;

namespace July.Bootstrap
{
    /// <summary>保存项目配置引用与启动查询结果，供公共步骤和项目模块共享。</summary>
    public sealed class LaunchStore : StoreBase
    {
        private readonly ScriptableObject _projectConfig;
        private LaunchInfo _current;

        /// <summary>配置由项目持有；Store 只保存引用，不销毁或释放配置资产。</summary>
        public LaunchStore(ScriptableObject projectConfig)
        {
            if (projectConfig == null)
                throw new ArgumentNullException(nameof(projectConfig), "启动配置资产未指定。");
            _projectConfig = projectConfig;
        }

        /// <summary>构造后即可获取项目配置；请求错误类型时直接暴露接入错误。</summary>
        public T GetProjectConfig<T>() where T : ScriptableObject => (T)_projectConfig;

        /// <summary>FetchConfigStep 成功后才可读取；提前读取违反启动顺序约定。</summary>
        public LaunchInfo Current => _current ?? throw new InvalidOperationException(
            "启动配置查询尚未成功，不能读取启动结果。");

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