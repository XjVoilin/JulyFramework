using System;
using UnityEngine;

namespace July.Release
{
    /// <summary>构建收集和客户端加载共用一份资源配置，由项目启动配置资产保存。</summary>
    [Serializable]
    public sealed class ReleaseResourceSettings
    {
        [Tooltip("YooAsset 包名称，构建与运行时初始化共用。")]
        public string packageName = "DefaultPackage";
        public string hotUpdateTag = "HotFix";
        public string aotMetaTag = "AotMeta";
        public string lobbyTag = "Lobby";
        public string buildinTag = "Buildin";
        [Tooltip("静态分析可能遗漏的 AOT 程序集文件名（含 .dll）。构建拷贝和运行时元数据加载共用此清单。")]
        public string[] mandatoryAotAssemblies = Array.Empty<string>();

        public string[] RequiredPreloadTags => new[] { aotMetaTag, hotUpdateTag, lobbyTag };
    }
}
