using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// 热更构建前校验并恢复指定持久备份；没有显式路径时沿用本地工作副本及归档规则。
    /// </summary>
    public sealed class AOTBackupRestoreStep : BuildStep
    {
        public override string Name => "恢复 AOT 备份（从持久目录）";

        public override string Validate(BuildContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.Platform))
                return "ctx.Platform 为空，无法定位平台对应的 AOT 备份";
            if (ctx.AotBackupInputPath != null)
            {
                AotBackupStore.RequireDisjoint(ctx.AotBackupInputPath, ReleaseProject.Profile.HybridCLR.AotBackupRoot);
                var workspace = HybridCLRBuildHelper.GetAOTBackupDir(ctx.Target, ctx.Platform, ctx.AOTBackupVersion);
                AotBackupStore.ValidateInput(ctx, workspace, ReleaseProject.Profile.HybridCLR.MandatoryAotAssemblies);
            }
            return null;
        }

        public override bool Execute(BuildContext ctx)
        {
            var localDir = HybridCLRBuildHelper.GetAOTBackupDir(ctx.Target, ctx.Platform, ctx.AOTBackupVersion);
            var archiveDir = ctx.AotBackupInputPath ?? BuildUtils.GetAOTArchiveDir(ctx.Target, ctx.Platform, ctx.AOTBackupVersion);
            AotBackupStore.Restore(ctx, localDir, archiveDir, ReleaseProject.Profile.HybridCLR.MandatoryAotAssemblies);
            Debug.Log($"[AOTRestore] AOT 工作副本已就绪: {localDir} (Platform={ctx.Platform}, CoreVersion={ctx.AOTBackupVersion}, Input={ctx.AotBackupInputPath ?? "本地规则"})");
            return true;
        }
    }
}
