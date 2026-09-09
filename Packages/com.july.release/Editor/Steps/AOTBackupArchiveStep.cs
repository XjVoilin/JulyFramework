using System.IO;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// 全量构建后将 AOT 备份拷贝到项目同级的持久目录，防止 workspace 清理后丢失。
    /// 路径：{项目根}/../AOTBackup/{项目名}/{target}/{CoreVersion}/
    /// </summary>
    public sealed class AOTBackupArchiveStep : BuildStep
    {
        public override string Name => "归档 AOT 备份到持久目录";

        public override string Validate(BuildContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.CoreVersion))
                return "ctx.CoreVersion 为空";
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

            var dst = BuildUtils.GetAOTArchiveDir(ctx.Target, ctx.Platform, ctx.CoreVersion);
            BuildUtils.CopyDirectory(src, dst);
            Debug.Log($"[AOTArchive] 已归档 AOT 备份 → {dst} (Platform={ctx.Platform})");
            return true;
        }
    }
}
