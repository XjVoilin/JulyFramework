using System;
using System.Collections.Generic;
using July.Analytics;
using July.Analytics.TencentDn;

internal static class RuntimeChecks
{
    private static int _assertions;

    private static void Main()
    {
        Check(!TencentDnAnalytics.Track(new MustNotSerializeEvent()), "Unconfigured does not serialize or call native");
        Check(!TencentDnAnalytics.SetOpenId("fixture"), "Unconfigured does not bind identity");
        TencentDnAnalytics.Configure(new TencentDnConfig { Enabled = false });
        Check(!TencentDnAnalytics.Track(new MustNotSerializeEvent()), "Disabled does not serialize or call native");
        Check(!TencentDnAnalytics.SetOpenId("fixture"), "Disabled does not bind identity");
        TencentDnAnalytics.Configure(new TencentDnConfig { Enabled = true });
        Check(!TencentDnAnalytics.Track(new MustNotSerializeEvent()), "Non-WeChat does not call native even when enabled");
        Check(!TencentDnAnalytics.SetOpenId("fixture"), "Non-WeChat does not bind identity");
        Console.WriteLine($"PASS: static reporting unsupported/disabled paths, {_assertions} assertions.");
    }

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private readonly struct MustNotSerializeEvent : IBIEvent
    {
        public string EventName => throw new InvalidOperationException("Disabled reporting accessed event");
        public Dictionary<string, object> ToParams() => throw new InvalidOperationException("Disabled reporting serialized event");
    }
}
