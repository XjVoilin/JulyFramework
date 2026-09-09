namespace July.Release.Editor
{
    /// <summary>
    /// 全量构建路径：GenerateAll（编译 DLL + IL2CPP 构建 + AOT 泛型分析）→ 拷贝 DLL 到资源目录。
    /// 确保 AOT 裁剪 DLL 和 AOTGenericReferences 与当前代码同步。
    /// </summary>
    public sealed class HybridCLRGenerateAllStep : BuildStep
    {
        public override string Name => "HybridCLR Generate All + 拷贝 DLL";

        public override string Validate(BuildContext ctx)
        {
            return HybridCLRBuildHelper.ValidateSettings() ? null : "HybridCLR 配置校验失败";
        }

        public override bool Execute(BuildContext ctx)
        {
            return HybridCLRBuildHelper.GenerateAllAndCopyDlls(ctx.Target);
        }
    }
}
