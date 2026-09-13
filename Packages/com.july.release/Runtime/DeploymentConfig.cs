using System;

namespace July.Release
{
    /// <summary>项目部署配置：运行环境、配置服务入口与资源下载根地址，供启动和发布工具共用。</summary>
    [Serializable]
    public sealed class DeploymentConfig
    {
        public ReleaseEnvironment Environment = ReleaseEnvironment.Dev;
        public string CdnBaseUrl = string.Empty;
        public ConfigServerUrls ConfigServerUrls = new();
    }

    [Serializable]
    public sealed class ConfigServerUrls
    {
        public string Dev = string.Empty;
        public string Test = string.Empty;
        public string Prod = string.Empty;

        public string Get(ReleaseEnvironment environment) => environment switch
        {
            ReleaseEnvironment.Dev => Dev,
            ReleaseEnvironment.Test => Test,
            ReleaseEnvironment.Prod => Prod,
            _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unknown release environment.")
        };
    }
}
