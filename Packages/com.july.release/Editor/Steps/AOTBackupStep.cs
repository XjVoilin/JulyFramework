using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// 全量构建后备份 AOT 裁剪 DLL + ScriptsAot 源码 hash，供后续热更构建使用。
    /// 以 ctx.CoreVersion 为备份目录名。
    /// 产物：
    ///   HybridCLRData/AOTBackup/{target}/{CoreVersion}/*.dll
    ///   HybridCLRData/AOTBackup/{target}/{CoreVersion}/aot-source.hash
    /// </summary>
    public sealed class AOTBackupStep : BuildStep
    {
        public override string Name => "备份 AOT 裁剪 DLL + 源码 hash";

        public override string Validate(BuildContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.CoreVersion))
                return "ctx.CoreVersion 为空（FullBuild 入口应先同步 CoreVersion = PlanVersion）";
            return null;
        }

        public override bool Execute(BuildContext ctx)
        {
            if (!HybridCLRBuildHelper.BackupAOTDlls(ctx.Target, ctx.Platform, ctx.CoreVersion))
                return false;

            var hash = AotSourceHasher.ComputeHash();
            if (string.IsNullOrEmpty(hash))
            {
                Debug.LogError("[AOTBackup] ScriptsAot 源码 hash 计算失败");
                return false;
            }

            AotSourceHasher.WriteHash(ctx.Target, ctx.Platform, ctx.CoreVersion, hash);
            Debug.Log(
                $"[AOTBackup] 存档 ScriptsAot 源码 hash → " +
                $"{AotSourceHasher.GetHashFilePath(ctx.Target, ctx.Platform, ctx.CoreVersion)} " +
                $"(hash={hash.Substring(0, 12)}…)");
            return true;
        }
    }
}
