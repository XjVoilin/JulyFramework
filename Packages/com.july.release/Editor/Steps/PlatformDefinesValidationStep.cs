using July.Release;
using UnityEditor;

namespace July.Release.Editor
{
    /// <summary>
    /// 构建前只读校验当前平台宏。宏变更会触发 Domain Reload，因此必须在独立的
    /// “切换平台”或 CI SyncPlatformDefines 阶段完成，流水线中不自动修改。
    /// </summary>
    public sealed class PlatformDefinesValidationStep : BuildStep
    {
        public override string Name => "平台编译环境校验";

        public override string Validate(BuildContext ctx)
        {
            if (ctx.Platform != PlatformKeys.WeChat && ctx.Platform != PlatformKeys.TikTok)
                return $"不支持的平台: {ctx.Platform}";

            if (EditorUserBuildSettings.activeBuildTarget != PlatformPreparation.PlatformBuildTarget)
            {
                return $"当前 BuildTarget 为 {EditorUserBuildSettings.activeBuildTarget}，" +
                       $"目标应为 {PlatformPreparation.PlatformBuildTarget}。请先切换平台。";
            }

            var mismatch = PlatformPreparation.DescribeDefineMismatch(ctx.Platform, ctx.Development);
            return string.IsNullOrEmpty(mismatch)
                ? null
                : $"当前编译宏与 {ctx.Platform}{(ctx.Development ? " Debug" : string.Empty)} 标准集合不一致（{mismatch}）。" +
                  "请先在构建工具中切换平台；CI 请先单独执行 SyncPlatformDefines。";
        }

        public override bool Execute(BuildContext ctx) => true;
    }
}