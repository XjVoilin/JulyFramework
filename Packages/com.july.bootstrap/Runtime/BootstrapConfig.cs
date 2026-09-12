using System;
using July.Analytics;
using July.Logging;
using July.Platform;
using July.Release;
using YooAsset;

namespace July.Bootstrap
{
    /// <summary>标准启动配置组合，直接持有各模块的配置，无需项目额外映射。</summary>
    [Serializable]
    public sealed class BootstrapConfig
    {
        public ReleaseSettings Release = new();
        public ThinkingDataConfig Analytics = new();
        public PlatformConfig Platform = new();
        public ResourceStartupConfig Resource = new();
        public HotUpdateConfig HotUpdate = new();
        public LogChannel EnabledLogChannels = LogChannel.All;

        internal void Validate()
        {
            if (Release == null || Release.ConfigServer == null || Analytics == null || Platform == null ||
                Resource == null || Resource.StartupDownloadTags == null || HotUpdate == null)
                throw new ArgumentException("Bootstrap configuration sections must be assigned.");
            if (!Enum.IsDefined(typeof(ReleaseEnvironment), Release.Env))
                throw new ArgumentException("Unknown release environment.");
            if (Analytics.Enabled && (string.IsNullOrWhiteSpace(Analytics.AppId) || string.IsNullOrWhiteSpace(Analytics.ServerUrl)))
                throw new ArgumentException("Enabled analytics requires AppId and ServerUrl.");
            if (Resource.PlayMode != EPlayMode.EditorSimulateMode && Resource.PlayMode != EPlayMode.OfflinePlayMode &&
                Resource.PlayMode != EPlayMode.HostPlayMode && Resource.PlayMode != EPlayMode.WebPlayMode)
                throw new ArgumentException("Unsupported standard resource play mode.");
            if (string.IsNullOrWhiteSpace(Resource.PackageName))
                throw new ArgumentException("Resource package name is required.");
            foreach (var tag in Resource.RequiredDownloadTags)
                if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentException("Startup resource tags cannot be empty.");
            if (string.IsNullOrWhiteSpace(HotUpdate.RegistrarAssembly) || string.IsNullOrWhiteSpace(HotUpdate.RegistrarType))
                throw new ArgumentException("Registrar assembly and type are required.");
            BootstrapAssemblyLoader.Normalize(HotUpdate.AdditionalAotMetadataAssemblies);
        }
    }

}
