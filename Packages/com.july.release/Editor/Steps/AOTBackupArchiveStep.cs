using System.IO;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// 全量构建后保存持久 AOT 备份。显式输出路径使用完整性清单和不可覆盖的发布；
    /// 未指定路径时沿用本地归档规则。
    /// </summary>
    public sealed class AOTBackupArchiveStep : BuildStep
    {
        public override string Name => "归档 AOT 备份到持久目录";

        public override string Validate(BuildContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.CoreVersion))
                return "ctx.CoreVersion 为空";
            if (ctx.AotBackupOutputPath != null)
            {
                AotBackupStore.RequireDisjoint(ctx.AotBackupOutputPath, ReleaseProject.Profile.HybridCLR.AotBackupRoot);
                var workspace = HybridCLRBuildHelper.GetAOTBackupDir(ctx.Target, ctx.Platform, ctx.CoreVersion);
                AotBackupStore.CheckOutput(workspace, ctx.AotBackupOutputPath);
            }
            return null;
        }

        public override bool Execute(BuildContext ctx)
        {
            var src = HybridCLRBuildHelper.GetAOTBackupDir(ctx.Target, ctx.Platform, ctx.CoreVersion);
            if (!Directory.Exists(src))
            {
                Debug.LogError($"[AOTArchive] workspace 内 AOT 备份不存在: {src}");
                return false;
            }

            var dst = ctx.AotBackupOutputPath ?? BuildUtils.GetAOTArchiveDir(ctx.Target, ctx.Platform, ctx.CoreVersion);
            var snapshot = AotBackupStore.Archive(ctx, src, dst, ReleaseProject.Profile.HybridCLR.MandatoryAotAssemblies);
            if (snapshot != null)
                Debug.Log($"[AOTBackup] SAVED path=\"{dst}\" platform={ctx.Platform} buildTarget={ctx.Target} " +
                          $"coreVersion={ctx.CoreVersion} files={snapshot.Manifest.files.Length} manifestSha256={snapshot.ManifestSha256}");
            else
                Debug.Log($"[AOTArchive] 已归档 AOT 备份 → {dst} (Platform={ctx.Platform})");
            return true;
        }
    }
}
