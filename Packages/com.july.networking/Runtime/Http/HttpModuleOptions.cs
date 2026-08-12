using System;

namespace July.Networking
{
    public class HttpModuleOptions
    {
        public string BaseUrl;
        public int TimeoutSeconds = 10;

        public int ReLoginCode;
        public int KickCode;

        public int MaxRetryCount = 3;
        public int RetryBaseDelayMs = 1000;
        public float RetryBackoffMultiplier = 2f;
        public int RetryMaxDelayMs = 10000;

        internal int CalculateRetryDelay(int attempt)
        {
            return (int)Math.Min(
                RetryBaseDelayMs * Math.Pow(RetryBackoffMultiplier, attempt),
                RetryMaxDelayMs);
        }
    }
}
