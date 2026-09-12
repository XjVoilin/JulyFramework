using July.Build;
using UnityEditor;

namespace July.Release.Editor
{
    /// <summary>Project paths and YooAsset policy for the framework HybridCLR implementation.</summary>
    public static class HybridCLRBuildHelper
    {
        static HybridCLRBuildProfile Profile => ReleaseProject.Profile.HybridCLR;
        public static bool ValidateSettings(bool logErrors = true)
        {
            ValidateCollectorDirectories();
            return HybridCLRBuildService.ValidateSettings(logErrors);
        }

        public static bool CompileAndCopyDlls(BuildTarget target, bool development)
        {
            if (!ValidateSettings()) return false;
            var success = HybridCLRBuildService.CompileAndCopyDlls(Profile, target, development, out var artifacts);
            return CompleteArtifacts(success, artifacts);
        }

        public static bool GenerateAllAndCopyDlls(BuildTarget target)
        {
            if (!ValidateSettings()) return false;
            var success = HybridCLRBuildService.GenerateAllAndCopyDlls(Profile, target, out var artifacts);
            return CompleteArtifacts(success, artifacts);
        }

        public static bool GenerateAll() => HybridCLRBuildService.GenerateAll();

        public static bool EnsureInstalled() => HybridCLRBuildService.EnsureInstalled();

        public static void ValidateCollectorDirectories()
        {
            var config = ReleaseProject.LoadBuildConfig();
            config.GetCollectorDirectory(ReleaseConventions.HotFixGroup);
            config.GetCollectorDirectory(ReleaseConventions.AotMetaGroup);
        }

        public static bool BackupAOTDlls(BuildTarget target, string platform, string version) =>
            HybridCLRBuildService.BackupAotDlls(Profile, target, platform, version);

        public static string GetAOTBackupDir(BuildTarget target, string platform, string version) =>
            HybridCLRBuildService.GetAotBackupDirectory(Profile, target, platform, version);

        public static string[] GetAvailableBackupVersions(BuildTarget target, string platform) =>
            HybridCLRBuildService.GetAvailableBackupVersions(
                Profile, target, platform, BuildUtils.GetAOTArchiveTargetRoot(target, platform));

        public static bool CompileHotUpdateOnly(BuildTarget target, string platform,
            string aotBackupVersion, bool development, bool strictMetadataCheck = false,
            bool stripAOT = true)
        {
            if (!ValidateSettings()) return false;
            var success = HybridCLRBuildService.CompileHotUpdateOnly(
                Profile, target, platform, aotBackupVersion, development,
                out var artifacts, strictMetadataCheck, stripAOT);
            return CompleteArtifacts(success, artifacts);
        }

        static bool CompleteArtifacts(bool success, HybridCLRBuildArtifacts artifacts)
        {
            if (success)
            {
                HybridClrAssemblyManifest.Write(Profile.HotUpdateDllDirectory,
                    artifacts.HotUpdateAssemblies, artifacts.AotMetadataAssemblies);
                AssetDatabase.Refresh();
            }
            return success;
        }
    }
}
