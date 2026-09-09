namespace July.Release.Editor
{
    /// <summary>
    /// 检测 HybridCLR 是否已安装，未安装则自动从 git 拉取并初始化。
    /// 幂等操作：已安装时直接跳过，耗时约 0ms。
    /// <para>必须排在 <see cref="HybridCLRGenerateAllStep"/> 之前。</para>
    /// </summary>
    public sealed class HybridCLRInstallStep : BuildStep
    {
        public override string Name => "HybridCLR 环境检测与安装";

        public override string Validate(BuildContext ctx) => null;

        public override bool Execute(BuildContext ctx)
        {
            return HybridCLRBuildHelper.EnsureInstalled();
        }
    }
}
