using System;

namespace July.Analytics.TencentDn
{
    /// <summary>项目保存的 SDK 接入配置；AppID 使用小游戏实际导出配置，不在这里重复维护。</summary>
    [Serializable]
    public sealed class TencentDnConfig
    {
        public bool Enabled;
        public long UserActionSetId;
        public string SecretKey = string.Empty;
    }

    /// <summary>运行配置向腾讯构建适配提供同一份设置，不要求配置资产具有特定类名或路径。</summary>
    public interface ITencentDnConfigProvider
    {
        TencentDnConfig TencentDn { get; }
    }
}
