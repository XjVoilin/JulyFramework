using System;
using System.Collections.Generic;
using July.Build;
using UnityEngine;
using UnityEditor;

namespace July.Release.Editor
{
    public interface IReleaseBuildConfig
    {
        UnityEngine.Object Asset { get; }
        string cloudUrl { get; }
        string planVersion { get; set; }
    }

    /// <summary>BuildConfig 和启动配置映射得到的构建参数，不另存配置资产。</summary>
    public sealed class ReleaseProjectProfile
    {
        public string BootConfigPath { get; set; }
        public string BuildConfigPath { get; set; }
        public string PackageName { get; set; }
        public string CdnRoot { get; set; }
        public string ExportRoot { get; set; }
        public string AotArchiveRoot { get; set; }
        public string AotSourceDirectory { get; set; }
        public string[] AotHashExclusions { get; set; }
        public HybridCLRBuildProfile HybridCLR { get; set; }
        public bool MergeSharedBundles { get; set; }
        public string[] BaseDefines { get; set; }
        public string SplashImagePath { get; set; }
        public bool DisableUnitySplash { get; set; }
        public int SharedBundleMergeDepth { get; set; }
        public string FontRoot { get; set; }
        public int FontMergeDepth { get; set; }
        public int MaxPreloadCount { get; set; }
        public long MaxPreloadBytes { get; set; }
        public IReadOnlyCollection<string> RequiredPreloadTags { get; set; }
    }

    /// <summary>框架装配点：读取项目配置资产，平台 SDK 适配器自行注册。</summary>
    public static class ReleaseProject
    {
        static BuildConfig _buildConfig;
        static readonly Dictionary<string, Func<IReleasePlatformBuilder>> Platforms = new();

        public static ReleaseProjectProfile Profile => LoadBuildConfig().CreateProfile();
        public static IReleaseBootConfig LoadBootConfig() => LoadBuildConfig().GetBootConfig();

        public static BuildConfig LoadBuildConfig()
        {
            if (_buildConfig != null) return _buildConfig;
            var guids = AssetDatabase.FindAssets("t:BuildConfig", new[] { "Assets" });
            if (guids.Length != 1)
                throw new InvalidOperationException($"项目 Assets 下必须有且仅有一个 July Release BuildConfig 资产，当前找到 {guids.Length} 个。请通过 Create > JulyGF > Build Config 创建，或移除重复资产。");
            _buildConfig = AssetDatabase.LoadAssetAtPath<BuildConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (_buildConfig == null)
                throw new InvalidOperationException("找到的 BuildConfig 资产类型不属于 July.Release.Editor.BuildConfig，请检查脚本引用。");
            return _buildConfig;
        }

        internal static bool HasPlatformBuilder(string platform) => Platforms.ContainsKey(platform);

        public static void RegisterPlatform(string platform, Func<IReleasePlatformBuilder> factory)
            => Platforms.Add(platform, factory);

        public static IReleasePlatformBuilder PlatformBuilder(string platform)
        {
            if (!Platforms.TryGetValue(platform, out var factory))
                throw new InvalidOperationException($"平台 {platform} 的构建适配未启用。请先切换平台编译宏，并确认项目已安装对应 SDK。");
            return factory();
        }
    }

    public interface IReleasePlatformBuilder
    {
        PlatformBuildArtifacts Build(BuildContext context);
        PlatformBuildArtifacts LocateArtifacts(BuildContext context);
    }

    public sealed class PlatformBuildArtifacts
    {
        public string ExportDirectory { get; }
        public string GameJsPath { get; }
        public string DataFilePath { get; }
        public PlatformBuildArtifacts(string exportDirectory, string gameJsPath, string dataFilePath = null)
        {
            ExportDirectory = exportDirectory;
            GameJsPath = gameJsPath;
            DataFilePath = dataFilePath;
        }
    }
}
