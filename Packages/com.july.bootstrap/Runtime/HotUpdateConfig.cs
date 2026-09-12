using System;
using UnityEngine;

namespace July.Bootstrap
{
    /// <summary>热更程序集加载和注册入口配置，不包含具体业务注册逻辑。</summary>
    [Serializable]
    public sealed class HotUpdateConfig
    {
        [Tooltip("自动生成清单之外，额外需要补充元数据的 AOT 程序集名称。构建和启动共用，不改变程序集的 AOT/热更划分。")]
        public string[] AdditionalAotMetadataAssemblies = Array.Empty<string>();

        [Tooltip("注册入口所在程序集的简单名称，不含 .dll；必须属于 HybridCLR Settings 配置的热更程序集。")]
        public string RegistrarAssembly;

        [Tooltip("实现 IHotUpdateRegistrar 的完整类型名，包含命名空间。")]
        public string RegistrarType;
    }
}
