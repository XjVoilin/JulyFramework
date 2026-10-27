using System;

namespace July.Logging
{
    internal static class JLogger
    {
        public static void LogError(object message) => throw new InvalidOperationException(message.ToString());
    }
}
