using System;
using System.IO;
using July.Build;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    [CreateAssetMenu(fileName = "BuildConfig", menuName = "JulyGF/Build Config")]
    public sealed class BuildConfig : ScriptableObject, IReleaseBuildConfig
    {
        [Tooltip("项目资源根 URL（COS 上传地址），允许携带路径前缀；必须与 BootConfig.cdnUrl 的路径前缀一致。")]
        public string cloudUrl = "";
        [Tooltip("资源内容版本；全量构建用于生成 CoreVersion，热更时可独立递增。")]
        public string planVersion = "0.1.0";
        [Tooltip("项目启动配置资产，必须实现 July.Release.IReleaseBootConfig。")]
        public ScriptableObject bootConfig;

        [Header("项目路径（相对项目根目录）")]
        public ReleaseBuildPaths paths = new();
        [Header("构建策略")]
        public string[] baseDefines = Array.Empty<string>();
        public string hotFixGroup = "HotFix";
        public string aotMetaGroup = "AOTMeta";
        [Min(1)] public int sharedBundleMergeDepth = 4;
        [Min(1)] public int fontMergeDepth = 5;
        [Min(1)] public int maxPreloadCount = 10;
        [Tooltip("预下载资源总大小上限，单位字节。")]
        public long maxPreloadBytes = 8L * 1024 * 1024;
        [Header("启动资源")]
        public TMP_FontAsset launchFont;
        public Texture2D splashImage;
        public bool disableUnitySplash = true;

        public UnityEngine.Object Asset => this;
        string IReleaseBuildConfig.cloudUrl => cloudUrl;
        string IReleaseBuildConfig.planVersion { get => planVersion; set => planVersion = value; }

        public IReleaseBootConfig GetBootConfig()
        {
            if (bootConfig == null || bootConfig is not IReleaseBootConfig config)
                throw new InvalidOperationException("请在 BuildConfig 的 Boot Config 中指定实现 IReleaseBootConfig 的启动配置资产。");
            return config;
        }

        internal ReleaseProjectProfile CreateProfile()
        {
            var boot = GetBootConfig();
            var resources = boot.Resources;
            var projectDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return new ReleaseProjectProfile
            {
                BootConfigPath = AssetDatabase.GetAssetPath(boot.Asset),
                BuildConfigPath = AssetDatabase.GetAssetPath(this),
                PackageName = resources.packageName,
                CdnRoot = paths.cdnRoot,
                ExportRoot = paths.exportRoot,
                AotArchiveRoot = Path.Combine(projectDirectory, paths.aotArchiveParent, new DirectoryInfo(projectDirectory).Name),
                AotSourceDirectory = paths.aotSourceDirectory,
                AotHashExclusions = paths.aotHashExclusions,
                HybridCLR = new HybridCLRBuildProfile(paths.hotUpdateDllDirectory, paths.aotMetadataDirectory,
                    paths.hybridClrBackupRoot, paths.aotGenericReferencesPath, resources.mandatoryAotAssemblies),
                CollectorSettingPath = paths.collectorSettingPath,
                MiniGamesRoot = paths.miniGamesRoot,
                ResourcesRoot = paths.resourcesRoot,
                HotFixGroup = hotFixGroup,
                AotMetaGroup = aotMetaGroup,
                HotFixTag = resources.hotUpdateTag,
                AotMetaTag = resources.aotMetaTag,
                LobbyTag = resources.lobbyTag,
                BaseDefines = baseDefines,
                LaunchFontGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(launchFont)),
                SplashImagePath = AssetDatabase.GetAssetPath(splashImage),
                DisableUnitySplash = disableUnitySplash,
                SharedBundleMergeDepth = sharedBundleMergeDepth,
                FontRoot = paths.fontRoot,
                FontMergeDepth = fontMergeDepth,
                MaxPreloadCount = maxPreloadCount,
                MaxPreloadBytes = maxPreloadBytes,
                RequiredPreloadTags = resources.RequiredPreloadTags,
                BuildinTag = resources.buildinTag,
#if UNITY_EDITOR_WIN
                CoscliPath = paths.coscliExecutable + ".exe",
#else
                CoscliPath = paths.coscliExecutable,
#endif
                CoscliConfigPath = paths.coscliConfigPath,
            };
        }
    }

    [Serializable]
    public sealed class ReleaseBuildPaths
    {
        public string cdnRoot = "CDN";
        public string exportRoot = "../Build";
        [Tooltip("未指定 AOT 输入/输出路径时使用的本地归档父目录；框架自动追加当前项目文件夹名。")]
        public string aotArchiveParent = "../AOTBackup";
        public string aotSourceDirectory = "Assets/Game/ScriptsAot";
        public string[] aotHashExclusions = { "HybridCLR" };
        public string hotUpdateDllDirectory = "Assets/Game/HotFixDlls";
        public string aotMetadataDirectory = "Assets/Game/AOTMetaDlls";
        public string hybridClrBackupRoot = "HybridCLRData/AOTBackup";
        public string aotGenericReferencesPath = "Assets/Game/ScriptsAot/Runtime/HybridCLR/AOTGenericReferences.cs";
        public string collectorSettingPath = "Assets/AssetBundleCollectorSetting.asset";
        public string miniGamesRoot = "Assets/Game/MiniGames";
        public string resourcesRoot = "Assets/Game/Res";
        public string fontRoot = "Assets/Game/Art/Fonts/";
        [Tooltip("COS CLI 路径，不含扩展名；Windows 自动追加 .exe。")]
        public string coscliExecutable = "Tools/coscli/coscli";
        public string coscliConfigPath = "Tools/coscli/.cos.yaml";
    }
}
