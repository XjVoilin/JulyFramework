using System;
using System.Collections.Generic;
using July.Build;
using UnityEngine;

namespace July.Release.Editor
{
    public interface IReleaseBootConfig
    {
        UnityEngine.Object Asset { get; }
        ReleaseEnvironment env { get; set; }
        string cdnUrl { get; }
        string EnvName { get; }
        string GetConfigServerUrl();
        string GetConfigServerUrl(ReleaseEnvironment environment);
    }

    public interface IReleaseBuildConfig
    {
        UnityEngine.Object Asset { get; }
        string cloudUrl { get; }
        string planVersion { get; set; }
    }

    /// <summary>Project-owned paths and resources. This is an in-memory binding, not another config asset.</summary>
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
        public string CollectorSettingPath { get; set; }
        public string MiniGamesRoot { get; set; }
        public string ResourcesRoot { get; set; }
        public string HotFixGroup { get; set; }
        public string AotMetaGroup { get; set; }
        public string HotFixTag { get; set; }
        public string AotMetaTag { get; set; }
        public string[] BaseDefines { get; set; }
        public string LaunchFontGuid { get; set; }
        public string SplashImagePath { get; set; }
        public bool DisableUnitySplash { get; set; }
        public int SharedBundleMergeDepth { get; set; }
        public string FontRoot { get; set; }
        public int FontMergeDepth { get; set; }
        public int MaxPreloadCount { get; set; }
        public long MaxPreloadBytes { get; set; }
        public IReadOnlyCollection<string> RequiredPreloadTags { get; set; }
        public string BuildinTag { get; set; }
        public string CoscliPath { get; set; }
        public string CoscliConfigPath { get; set; }
    }

    /// <summary>The editor composition root. The project binds existing assets and installed platform SDKs once.</summary>
    public static class ReleaseProject
    {
        static ReleaseProjectProfile _profile;
        static Func<IReleaseBootConfig> _loadBoot;
        static Func<IReleaseBuildConfig> _loadBuild;
        static Func<string, IReleasePlatformBuilder> _platform;
        public static ReleaseProjectProfile Profile => _profile ?? throw new InvalidOperationException(
            "Release project is not configured. Install the project's editor binding before running release tools.");

        public static void Configure(ReleaseProjectProfile profile, Func<IReleaseBootConfig> loadBoot,
            Func<IReleaseBuildConfig> loadBuild, Func<string, IReleasePlatformBuilder> platform)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _loadBoot = loadBoot ?? throw new ArgumentNullException(nameof(loadBoot));
            _loadBuild = loadBuild ?? throw new ArgumentNullException(nameof(loadBuild));
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        }
        public static IReleaseBootConfig LoadBootConfig() { _ = Profile; return _loadBoot(); }
        public static IReleaseBuildConfig LoadBuildConfig() { _ = Profile; return _loadBuild(); }
        public static IReleasePlatformBuilder PlatformBuilder(string platform) { _ = Profile; return _platform(platform); }
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
