using System;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using July.Build;
using TMPro;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace July.Release.Editor
{
    [CreateAssetMenu(fileName = "BuildConfig", menuName = "JulyGF/Build Config")]
    public sealed class BuildConfig : ScriptableObject, IReleaseBuildConfig
    {
        [Tooltip("项目 COS 根 URL，允许路径前缀；前缀必须与运行配置中的 CDN 一致。")]
        public string cloudUrl = "";
        [Tooltip("内容版本；全量构建生成 CoreVersion，热更时可独立递增。")]
        public string planVersion = "0.1.0";
        [Tooltip("项目运行配置资产，实现 IReleaseBootConfig；不限定资产类型或名称。")]
        public ScriptableObject bootConfig;
        public string[] baseDefines = Array.Empty<string>();

        [Tooltip("引用项目现有 YooAsset 收集配置，不复制收集规则。")]
        public AssetBundleCollectorSetting collectorSettings;
        [Tooltip("仅在运行配置不提供共享资源契约时使用。")]
        public ReleaseResourceSettings resources = new();
        public ReleaseAotSettings aot = new();

        public SharedBundleSettings sharedBundles = new();
        [Tooltip("可选：构建期间替换 TMP 默认字体。留空保持项目原有字体。")]
        public TMP_FontAsset launchFont;
        public Texture2D splashImage;
        public bool disableUnitySplash = true;
        [Min(1)] public int maxPreloadCount = 10;
        public long maxPreloadBytes = 8L * 1024 * 1024;

        // 工作副本和本地 CDN 是框架目录约定，不是项目接入参数。
        internal const string CdnDirectory = "CDN";
        internal const string AotWorkspaceDirectory = "HybridCLRData/AOTBackup";
        public UnityEngine.Object Asset => this;
        string IReleaseBuildConfig.cloudUrl => cloudUrl;
        string IReleaseBuildConfig.planVersion { get => planVersion; set => planVersion = value; }

        public IReleaseBootConfig GetBootConfig()
        {
            if (bootConfig is not IReleaseBootConfig config)
                throw new InvalidOperationException("请在 BuildConfig 中指定实现 IReleaseBootConfig 的项目运行配置资产。");
            return config;
        }

        internal ReleaseResourceSettings ResourceSettings =>
            GetBootConfig() is IReleaseResourceConfig shared ? shared.Resources : resources;

        internal string GetCollectorDirectory(string groupName)
        {
            if (collectorSettings == null)
                throw new InvalidOperationException("请在 BuildConfig 中引用项目现有 YooAsset 收集配置。");
            var package = collectorSettings.Packages.SingleOrDefault(x => x.PackageName == ResourceSettings.packageName);
            if (package == null)
                throw new InvalidOperationException($"收集配置中没有资源包 {ResourceSettings.packageName}。");
            var group = package.Groups.SingleOrDefault(x => x.GroupName == groupName);
            if (group == null || group.Collectors.Count != 1)
                throw new InvalidOperationException($"分组 {groupName} 必须有且仅有一个 DLL 收集目录；请在收集配置中按框架约定设置 HotFix / AOTMeta 分组。");
            var path = group.Collectors[0].CollectPath;
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException($"DLL 分组 {groupName} 的收集路径不能为空。");
            path = path.Replace('\\', '/').TrimEnd('/');
            // 这些目录会接收生成文件，不能让配置指向工程外、整个 Assets 或一个文件。
            var full = Path.GetFullPath(path);
            var assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase) ||
                File.Exists(full))
                throw new InvalidOperationException($"DLL 收集路径必须是 Assets 下的子目录：{path}");
            return path;
        }

        internal ReleaseProjectProfile CreateProfile()
        {
            var boot = GetBootConfig();
            var resource = ResourceSettings;
            var projectDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var hotUpdateDirectory = GetCollectorDirectory(ReleaseConventions.HotFixGroup);
            var metadataDirectory = GetCollectorDirectory(ReleaseConventions.AotMetaGroup);
            if (PathsOverlap(hotUpdateDirectory, metadataDirectory))
                throw new InvalidOperationException("热更 DLL 与 AOT 元数据收集目录不能相同或互为父子目录。");
            return new ReleaseProjectProfile
            {
                BootConfigPath = AssetDatabase.GetAssetPath(boot.Asset),
                BuildConfigPath = AssetDatabase.GetAssetPath(this),
                PackageName = resource.packageName,
                CdnRoot = CdnDirectory,
                ExportRoot = ReleaseConventions.ExportRoot,
                AotArchiveRoot = Path.Combine(projectDirectory, ReleaseConventions.LocalAotArchiveParent, new DirectoryInfo(projectDirectory).Name),
                AotSourceDirectory = aot.sourceDirectory,
                AotHashExclusions = aot.hashExclusions,
                HybridCLR = new HybridCLRBuildProfile(hotUpdateDirectory, metadataDirectory, AotWorkspaceDirectory,
                    Path.Combine("Assets", SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile), resource.mandatoryAotAssemblies),
                MergeSharedBundles = sharedBundles.enabled,
                HotFixTag = resource.hotUpdateTag,
                AotMetaTag = resource.aotMetaTag,
                BaseDefines = baseDefines,
                SplashImagePath = AssetDatabase.GetAssetPath(splashImage),
                DisableUnitySplash = disableUnitySplash,
                SharedBundleMergeDepth = sharedBundles.mergeDepth,
                FontRoot = sharedBundles.fontDirectory,
                FontMergeDepth = sharedBundles.fontMergeDepth,
                MaxPreloadCount = maxPreloadCount,
                MaxPreloadBytes = maxPreloadBytes,
                RequiredPreloadTags = resource.RequiredPreloadTags,
                BuildinTag = resource.buildinTag,
            };
        }

        internal static bool PathsOverlap(string left, string right)
        {
            left = Path.GetFullPath(left).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
            right = Path.GetFullPath(right).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
            return left.StartsWith(right, StringComparison.OrdinalIgnoreCase) || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    public sealed class ReleaseAotSettings
    {
        [Tooltip("参与热更安全检查的项目 AOT 源码目录。")]
        public string sourceDirectory = "Assets/Game/ScriptsAot";
        public string[] hashExclusions = { "HybridCLR" };
    }

    [Serializable]
    public sealed class SharedBundleSettings
    {
        public bool enabled;
        [Min(1)] public int mergeDepth = 4;
        [Tooltip("可选：字体目录使用独立合并深度；留空使用普通规则。")]
        public string fontDirectory = "";
        [Min(1)] public int fontMergeDepth = 5;
    }
}
