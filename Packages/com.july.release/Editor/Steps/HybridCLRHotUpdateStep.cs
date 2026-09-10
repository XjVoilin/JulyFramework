using System.IO;

namespace July.Release.Editor
{
    /// <summary>
    /// 热更构建路径：编译热更 DLL → MissingMetadataChecker 检测 → 从 AOT 备份拷贝并瘦身。
    /// </summary>
    public sealed class HybridCLRHotUpdateStep : BuildStep
    {
        public override string Name => "HybridCLR 热更编译 + 元数据检测";

        public override string Validate(BuildContext ctx)
        {
            if (!HybridCLRBuildHelper.ValidateSettings())
                return "HybridCLR 配置校验失败";

            if (string.IsNullOrEmpty(ctx.AOTBackupVersion))
                return "未指定 AOT 备份版本";

            // BuildRunner 先执行全部 Validate；此时工作目录尚未恢复。
            // 指定输入已由 AOTBackupRestoreStep.Validate 校验，不检查旧目录或旧归档。
            if (ctx.AotBackupInputPath != null) return null;

            var backupDir = HybridCLRBuildHelper.GetAOTBackupDir(ctx.Target, ctx.Platform, ctx.AOTBackupVersion);
            if (!Directory.Exists(backupDir) && !Directory.Exists(
                    BuildUtils.GetAOTArchiveDir(ctx.Target, ctx.Platform, ctx.AOTBackupVersion)))
                return $"AOT 备份目录不存在: {backupDir} (Platform={ctx.Platform})\n请先执行一次全量构建";

            return null;
        }

        public override bool Execute(BuildContext ctx)
        {
            if (ctx.AotBackupInputPath != null)
            {
                var workspace = HybridCLRBuildHelper.GetAOTBackupDir(ctx.Target, ctx.Platform, ctx.AOTBackupVersion);
                AotBackupStore.ValidateRestored(ctx, workspace, ReleaseProject.Profile.HybridCLR.MandatoryAotAssemblies);
            }
            return HybridCLRBuildHelper.CompileHotUpdateOnly(
                ctx.Target, ctx.Platform, ctx.AOTBackupVersion, ctx.Development, ctx.StrictMetadataCheck);
        }
    }
}
