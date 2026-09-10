using System;

namespace July.Release.Editor
{
    /// <summary>只处理显式 AOT 目录参数；不引入机器或构建模式配置。</summary>
    public static class AotBackupArguments
    {
        public static void Apply(string[] args, string entryPoint, BuildContext context)
        {
            string input = null, output = null;
            for (var i = 0; i < args.Length; i++)
            {
                var flag = args[i];
                if (flag != "-aotBackupInputPath" && flag != "-aotBackupOutputPath") continue;
                if (i + 1 == args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new ArgumentException($"{flag} 缺少目录参数值。");
                var value = AotBackupStore.AbsolutePath(args[++i]);
                if (flag == "-aotBackupInputPath")
                {
                    if (input != null) throw new ArgumentException($"{flag} 不能重复指定。");
                    input = value;
                }
                else
                {
                    if (output != null) throw new ArgumentException($"{flag} 不能重复指定。");
                    output = value;
                }
            }
            if (input != null && output != null)
                throw new ArgumentException("-aotBackupInputPath 与 -aotBackupOutputPath 不能同时使用。");
            if (input != null && entryPoint != nameof(BuildPipelineCI.HotUpdateBuild))
                throw new ArgumentException("-aotBackupInputPath 仅支持 HotUpdateBuild。");
            if (output != null && entryPoint != nameof(BuildPipelineCI.FullBuild))
                throw new ArgumentException("-aotBackupOutputPath 仅支持 FullBuild。");
            if (input != null)
            {
                // 旧参数解析缺值时会返回 null；显式输入不能因此丢失调用方的版本断言。
                var assertionIndex = Array.IndexOf(args, "-aotBackupVersion");
                if (assertionIndex >= 0 && (assertionIndex != Array.LastIndexOf(args, "-aotBackupVersion") ||
                    assertionIndex + 1 == args.Length || !BuildUtils.IsValidVersion(args[assertionIndex + 1])))
                    throw new ArgumentException("指定 AOT 输入路径时，-aotBackupVersion 必须是唯一且有效的版本断言。");
            }
            context.AotBackupInputPath = input;
            context.AotBackupOutputPath = output;
        }
    }
}
