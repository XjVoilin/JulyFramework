using System;

namespace July.Analytics
{
    /// <summary>可序列化的项目配置；SDK 运行参数在启动时组装。</summary>
    [Serializable]
    public sealed class ThinkingDataConfig
    {
        public bool Enabled = true;
        public string AppId = string.Empty;
        public string ServerUrl = string.Empty;
        public bool EnableLog = true;
    }
}
