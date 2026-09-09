using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// HotUpdate 前置检查：对比当前 ScriptsAot 源码 hash 与 AOT 备份归档里的 hash。
    /// 不一致 → 硬失败，提示「AOT 代码已修改，必须使用 FullBuild」。
    ///
    /// 位置：在 AOTBackupRestoreStep 之后（确保 hash 文件已在 workspace），
    ///      在 HybridCLRHotUpdateStep 之前（确保检测先于编译热更 DLL）。
    /// </summary>
    public sealed class AotSourceHashStep : BuildStep
    {
        public override string Name => "ScriptsAot 源码 hash 校验";

        public override string Validate(BuildContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.AOTBackupVersion))
                return "ctx.AOTBackupVersion 为空，AotSourceHashStep 需要知道对比哪个备份的 hash";
            return null;
        }

        public override bool Execute(BuildContext ctx)
        {
            var archivedHash = AotSourceHasher.ReadHash(ctx.Target, ctx.Platform, ctx.AOTBackupVersion);
            if (string.IsNullOrEmpty(archivedHash))
            {
                Debug.LogWarning(
                    $"[AotSourceHash] AOT 备份 {ctx.AOTBackupVersion} (Platform={ctx.Platform}) 没有 {AotSourceHasher.HashFileName}，" +
                    "可能是旧版本构建系统产出的遗留备份。本次放行；下次 FullBuild 会补上 hash 文件。\n" +
                    $"查询路径: {AotSourceHasher.GetHashFilePath(ctx.Target, ctx.Platform, ctx.AOTBackupVersion)}");
                return true;
            }

            var currentHash = AotSourceHasher.ComputeHash();
            if (string.IsNullOrEmpty(currentHash))
            {
                Debug.LogError("[AotSourceHash] 当前 ScriptsAot 源码 hash 计算失败");
                return false;
            }

            if (string.Equals(archivedHash, currentHash, System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[AotSourceHash] ScriptsAot hash 一致 → {currentHash.Substring(0, 12)}… 放行");
                return true;
            }

            var currentDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                PlatformPanel.PlatformBuildTargetGroup);
            Debug.LogError(
                "[AotSourceHash] AOT 环境不一致，必须使用 FullBuild。\n" +
                "  可能原因：ScriptsAot 源码变更、平台宏切换（WeChat↔TikTok）、或 JULYGF_DEBUG 状态变更。\n" +
                $"  AOT 备份版本:    {ctx.AOTBackupVersion}\n" +
                $"  归档 hash:        {archivedHash}\n" +
                $"  当前 hash:        {currentHash}\n" +
                $"  当前 Defines:     {currentDefines}\n" +
                "  解决：使用 BuildPipelineCI.FullBuild 入口重新打包主包。");
            return false;
        }
    }
}
