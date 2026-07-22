using System;
using System.Collections.Generic;

namespace July.Build
{
    /// <summary>Project-owned paths and assembly policy consumed by HybridCLR build operations.</summary>
    public sealed class HybridCLRBuildProfile
    {
        public string HotUpdateDllDirectory { get; }
        public string AotMetadataDirectory { get; }
        public string AotBackupRoot { get; }
        public string AotGenericReferencesPath { get; }
        public IReadOnlyList<string> MandatoryAotAssemblies { get; }

        public HybridCLRBuildProfile(
            string hotUpdateDllDirectory,
            string aotMetadataDirectory,
            string aotBackupRoot,
            string aotGenericReferencesPath,
            IReadOnlyList<string> mandatoryAotAssemblies = null)
        {
            HotUpdateDllDirectory = RequirePath(hotUpdateDllDirectory,
                nameof(hotUpdateDllDirectory));
            AotMetadataDirectory = RequirePath(aotMetadataDirectory,
                nameof(aotMetadataDirectory));
            AotBackupRoot = RequirePath(aotBackupRoot, nameof(aotBackupRoot));
            AotGenericReferencesPath = RequirePath(aotGenericReferencesPath,
                nameof(aotGenericReferencesPath));
            MandatoryAotAssemblies = mandatoryAotAssemblies ?? Array.Empty<string>();
        }

        private static string RequirePath(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Path cannot be empty.", parameterName);
            return value.Replace('\\', '/').TrimEnd('/');
        }
    }
}
