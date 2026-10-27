using System;
using System.Collections.Generic;

namespace July.Release.Editor
{
    /// <summary>平台 SDK 导出后处理契约；校验在构建开始前执行，处理失败由现有流水线报告。</summary>
    public interface IReleaseExportProcessor
    {
        string Name { get; }
        string Validate(BuildContext context, IReleaseBootConfig bootConfig);
        void Process(BuildContext context, IReleaseBootConfig bootConfig, PlatformBuildArtifacts artifacts);
    }

    /// <summary>可选包按名称注册处理器，使用固定顺序执行，不改变平台 SDK 的注册与导出职责。</summary>
    public sealed class ReleaseExportProcessorRegistry
    {
        private readonly SortedDictionary<string, IReleaseExportProcessor> _processors = new(StringComparer.Ordinal);

        public void Register(IReleaseExportProcessor processor) => _processors.Add(processor.Name, processor);

        public string Validate(BuildContext context, IReleaseBootConfig bootConfig)
        {
            foreach (var processor in _processors.Values)
            {
                var error = processor.Validate(context, bootConfig);
                if (!string.IsNullOrEmpty(error)) return $"{processor.Name}: {error}";
            }
            return null;
        }

        public void Process(BuildContext context, IReleaseBootConfig bootConfig, PlatformBuildArtifacts artifacts)
        {
            foreach (var processor in _processors.Values)
                processor.Process(context, bootConfig, artifacts);
        }
    }
}
