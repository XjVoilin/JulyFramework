using System.Collections.Generic;
using July.Analytics;
using July.Arch;

namespace July.Bootstrap
{
    /// <summary>公共启动事件协议；项目特有的启动类型由项目提供。</summary>
    public static class LaunchTelemetry
    {
        public static void Report(string stepId, int launchType = 1, bool flush = false)
        {
            var system = ArchContext.Current.GetSystem<IAnalyticsSystem>();
            system.Track("launch", new Dictionary<string, object>(2)
            {
                { "step_id", stepId },
                { "type", launchType }
            });
            if (flush) system.Flush();
        }
    }
}
