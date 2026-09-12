using System;

namespace July.Networking
{
    /// <summary>网络传输配置；业务响应码和处理器仍由项目负责。</summary>
    [Serializable]
    public struct HttpConfig
    {
        public int TimeoutSeconds;
        public int MaxRetryCount;
        public int RetryBaseDelayMs;
        public float RetryBackoffMultiplier;
        public int RetryMaxDelayMs;

        public static HttpConfig Default => new()
        {
            TimeoutSeconds = 10,
            MaxRetryCount = 3,
            RetryBaseDelayMs = 1000,
            RetryBackoffMultiplier = 2f,
            RetryMaxDelayMs = 10000
        };
    }
}
