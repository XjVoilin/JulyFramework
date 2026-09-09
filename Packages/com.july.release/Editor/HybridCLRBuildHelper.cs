using System.Collections.Generic;
using July.Release;
using July.Build;
using July.Resource.YooAsset;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>Project paths and YooAsset policy for the framework HybridCLR implementation.</summary>
    public static class HybridCLRBuildHelper
    {
        static HybridCLRBuildProfile Profile => ReleaseProject.Profile.HybridCLR;
        static string CollectorSettingPath => ReleaseProject.Profile.CollectorSettingPath;
        static IReadOnlyList<YooAssetCollectorGroupDefinition> CollectorGroups => new[]
        {
            new YooAssetCollectorGroupDefinition(ReleaseProject.Profile.HotFixGroup,
                "HybridCLR hot-update DLLs", ReleaseProject.Profile.HotFixTag, Profile.HotUpdateDllDirectory),
            new YooAssetCollectorGroupDefinition(ReleaseProject.Profile.AotMetaGroup,
                "HybridCLR supplemental AOT metadata", ReleaseProject.Profile.AotMetaTag, Profile.AotMetadataDirectory),
        };

        public static bool ValidateSettings(bool logErrors = true)
        {
            var sdkValid = HybridCLRBuildService.ValidateSettings(logErrors);
            var collectorsValid = HasRequiredABGroups();
            if (!collectorsValid && logErrors)
            {
                Debug.LogError(
                    "[HybridCLR] YooAsset HotFix/AOTMeta groups are missing. " +
                    "Run the collector initialization action first.");
            }
            return sdkValid && collectorsValid;
        }

        public static bool CompileAndCopyDlls(BuildTarget target, bool development) =>
            ValidateSettings() && HybridCLRBuildService.CompileAndCopyDlls(
                Profile, target, development);

        public static bool GenerateAllAndCopyDlls(BuildTarget target) =>
            ValidateSettings() && HybridCLRBuildService.GenerateAllAndCopyDlls(Profile, target);

        public static bool GenerateAll() => HybridCLRBuildService.GenerateAll();

        public static bool EnsureInstalled() => HybridCLRBuildService.EnsureInstalled();

        public static void EnsureABCollector()
        {
            if (YooAssetCollectorConfigurator.EnsureGroups(
                    CollectorSettingPath, BuildUtils.PackageName, CollectorGroups))
                Debug.Log("[HybridCLR] YooAsset HotFix/AOTMeta collector groups are ready.");
        }

        public static bool HasRequiredABGroups() =>
            YooAssetCollectorConfigurator.HasGroups(
                CollectorSettingPath, BuildUtils.PackageName, CollectorGroups);

        public static bool BackupAOTDlls(BuildTarget target, string platform, string version) =>
            HybridCLRBuildService.BackupAotDlls(Profile, target, platform, version);

        public static string GetAOTBackupDir(BuildTarget target, string platform, string version) =>
            HybridCLRBuildService.GetAotBackupDirectory(Profile, target, platform, version);

        public static string[] GetAvailableBackupVersions(BuildTarget target, string platform) =>
            HybridCLRBuildService.GetAvailableBackupVersions(
                Profile, target, platform, BuildUtils.GetAOTArchiveTargetRoot(target, platform));

        public static bool CompileHotUpdateOnly(BuildTarget target, string platform,
            string aotBackupVersion, bool development, bool strictMetadataCheck = false,
            bool stripAOT = true) =>
            ValidateSettings() && HybridCLRBuildService.CompileHotUpdateOnly(
                Profile, target, platform, aotBackupVersion, development,
                strictMetadataCheck, stripAOT);
    }
}
