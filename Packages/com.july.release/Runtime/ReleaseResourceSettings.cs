using System;
using System.Linq;
using UnityEngine;

namespace July.Release
{
    /// <summary>构建和运行时共用的资源分发设置；不包含程序集加载配置。</summary>
    [Serializable]
    public class ReleaseResourceSettings
    {
        [Tooltip("YooAsset 资源包名称，构建与运行时初始化共用。")]
        public string PackageName = "DefaultPackage";

        [Tooltip("进入游戏前必须下载的资源标签，例如 Lobby 或 Startup。热更和 AOT 标签由框架自动加入；下载不代表加载进内存。")]
        public string[] StartupDownloadTags = Array.Empty<string>();

        /// <summary>构建预下载和启动下载共用的完整标签清单，不需要项目重复填写代码标签。</summary>
        public string[] RequiredDownloadTags => new[]
        {
            ReleaseResourceConventions.AotMetadataTag,
            ReleaseResourceConventions.HotUpdateTag
        }.Concat(StartupDownloadTags).Distinct(StringComparer.Ordinal).ToArray();
    }
}
