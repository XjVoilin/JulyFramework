using System;
using YooAsset;

namespace July.Resource.YooAsset
{
    public sealed class YooAssetOptions
    {
        public string PackageName { get; set; } = "DefaultPackage";
        public EPlayMode PlayMode { get; set; } = EPlayMode.OfflinePlayMode;
        public string DefaultHostServer { get; set; }
        public string FallbackHostServer { get; set; }
        public int MaxConcurrentDownloads { get; set; } = 10;
        public int DownloadRetryCount { get; set; } = 3;
        public bool SetAsDefaultPackage { get; set; } = true;
        public bool UpdateManifestAfterInitialization { get; set; } = true;

        /// <summary>
        /// 覆盖默认初始化参数。小游戏文件系统等平台差异应从这里注入。
        /// </summary>
        public Func<ResourcePackage, InitializeParameters> CreateInitializeParameters { get; set; }

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(PackageName))
                throw new InvalidOperationException("YooAsset PackageName 不能为空。");
            if (MaxConcurrentDownloads <= 0)
                throw new InvalidOperationException("MaxConcurrentDownloads 必须大于 0。");
            if (DownloadRetryCount < 0)
                throw new InvalidOperationException("DownloadRetryCount 不能小于 0。");
            if (PlayMode == EPlayMode.HostPlayMode && CreateInitializeParameters == null &&
                string.IsNullOrWhiteSpace(DefaultHostServer))
                throw new InvalidOperationException("HostPlayMode 必须配置 DefaultHostServer。");
            if (PlayMode == EPlayMode.CustomPlayMode && CreateInitializeParameters == null)
                throw new InvalidOperationException("CustomPlayMode 必须提供 CreateInitializeParameters。");
        }
    }
}
