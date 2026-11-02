using System.Collections.Generic;

namespace July.AssetCheck
{
    // 两种输入范围共用同一个逐资源执行入口；不在规则层掺入目录、忽略或报告存储。
    internal static class AssetCheckExecution
    {
        internal static IEnumerable<bool> Check(IAssetCheckModule module, string path, PlatformStandards standards,
            List<AssetCheckIssue> issues)
        {
            if (module is IIncrementalAssetCheckModule incremental)
            {
                foreach (var step in incremental.CheckIncrementally(path, standards, issues)) yield return step;
            }
            else issues.AddRange(module.Check(path, standards));
        }
    }
}
