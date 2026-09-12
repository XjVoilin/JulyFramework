using System;

namespace July.Release
{
    /// <summary>运行时启动和编辑器发布工具共用的项目配置。</summary>
    [Serializable]
    public sealed class ReleaseSettings
    {
        public ReleaseEnvironment Env = ReleaseEnvironment.Dev;
        public string CdnUrl = string.Empty;
        public ConfigServerUrls ConfigServer = new();
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
