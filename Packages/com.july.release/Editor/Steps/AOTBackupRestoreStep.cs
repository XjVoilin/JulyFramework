using System.IO;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// 热更构建前检查 workspace 内 AOT 备份是否存在，缺失时从项目同级持久目录恢复。
    /// 路径：{项目根}/../AOTBackup/{项目名}/{target}/{version}/
    /// </summary>
    public sealed class AOTBackupRestoreStep : BuildStep
    {
        public override string Name => "恢复 AOT 备份（从持久目录）";

        public override string Validate(BuildContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.Platform))
                return "ctx.Platform 为空，无法定位平台对应的 AOT 备份";
            return null;
        }

        public override bool Execute(BuildContext ctx)
        {
            var localDir = HybridCLRBuildHelper.GetAOTBackupDir(ctx.Target, ctx.Platform, ctx.AOTBackupVersion);
            if (Directory.Exists(localDir))
            {
                Debug.Log($"[AOTRestore] workspace 内备份已存在，跳过: {localDir} (Platform={ctx.Platform})");
                return true;
            }

            var archiveDir = BuildUtils.GetAOTArchiveDir(ctx.Target, ctx.Platform, ctx.AOTBackupVersion);
            if (!Directory.Exists(archiveDir))
            {
                Debug.LogError(
                    $"[AOTRestore] AOT 备份不存在 (Platform={ctx.Platform}):\n" +
                    $"  workspace: {localDir}\n" +
                    $"  持久目录:  {archiveDir}\n" +
                    "请先执行一次全量构建");
                return false;
            }

            BuildUtils.CopyDirectory(archiveDir, localDir);
            Debug.Log($"[AOTRestore] 已从持久目录恢复 AOT 备份: {archiveDir} → {localDir} (Platform={ctx.Platform})");
            return true;
        }
    }
}
