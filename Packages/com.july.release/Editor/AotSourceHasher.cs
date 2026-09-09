using UnityEditor;

namespace July.Release.Editor
{
    /// <summary>Project AOT source-tree policy for the framework hash implementation.</summary>
    public static class AotSourceHasher
    {
        public static string ScriptsAotDir => ReleaseProject.Profile.AotSourceDirectory;
        public const string HashFileName = July.Build.AotSourceHasher.HashFileName;

        public static string ComputeHash() => July.Build.AotSourceHasher.ComputeHash(
            ScriptsAotDir, PlatformPanel.PlatformBuildTargetGroup, ReleaseProject.Profile.AotHashExclusions);

        public static string GetHashFilePath(BuildTarget target, string platform,
            string coreVersion) => July.Build.AotSourceHasher.GetHashFilePath(
            HybridCLRBuildHelper.GetAOTBackupDir(target, platform, coreVersion));

        public static void WriteHash(BuildTarget target, string platform,
            string coreVersion, string hash) => July.Build.AotSourceHasher.WriteHash(
            HybridCLRBuildHelper.GetAOTBackupDir(target, platform, coreVersion), hash);

        public static string ReadHash(BuildTarget target, string platform,
            string coreVersion) => July.Build.AotSourceHasher.ReadHash(
            HybridCLRBuildHelper.GetAOTBackupDir(target, platform, coreVersion));
    }
}
