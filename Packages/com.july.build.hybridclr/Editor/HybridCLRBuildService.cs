using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using HybridCLR.Editor.AOT;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.HotUpdate;
using HybridCLR.Editor.Installer;
using UnityEditor;
using UnityEngine;

namespace July.Build
{
    /// <summary>
    /// Strongly typed HybridCLR 8.7 build implementation.
    /// Project-owned paths remain explicit in <see cref="HybridCLRBuildProfile"/>.
    /// </summary>
    public static class HybridCLRBuildService
    {
        public static bool ValidateSettings(bool logErrors = true)
        {
            if (GetHotUpdateAssemblyNames().Count > 0)
                return true;

            if (logErrors)
                Debug.LogError("[HybridCLR] No hot-update assemblies are configured.");
            return false;
        }

        public static bool CompileAndCopyDlls(HybridCLRBuildProfile profile,
            BuildTarget target, bool development)
        {
            if (!ValidateSettings()) return false;
            try
            {
                EditorUtility.DisplayProgressBar("HybridCLR", "Compiling hot-update DLLs...", 0.2f);
                if (!CompileDlls(target, development)) return false;

                EditorUtility.DisplayProgressBar("HybridCLR", "Copying DLLs...", 0.6f);
                if (!CopyHotUpdateDlls(profile, target)) return false;

                CopyCurrentAotMetadata(profile, target);
                AssetDatabase.Refresh();
                return true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Installs HybridCLR when its local libil2cpp environment is absent.</summary>
        public static bool EnsureInstalled()
        {
            try
            {
                var controller = new InstallerController();
                if (controller.HasInstalledHybridCLR())
                {
                    Debug.Log($"[HybridCLR] Already installed (version: {controller.InstalledLibil2cppVersion}).");
                    return true;
                }

                Debug.Log("[HybridCLR] Installing the default libil2cpp environment...");
                controller.InstallDefaultHybridCLR();
                if (!controller.HasInstalledHybridCLR())
                {
                    Debug.LogError("[HybridCLR] Installation validation failed. Check git and network access.");
                    return false;
                }

                Debug.Log($"[HybridCLR] Installation completed (version: {controller.InstalledLibil2cppVersion}).");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[HybridCLR] Installation failed: {exception.Message}");
                return false;
            }
        }

        /// <summary>Runs HybridCLR Generate All without project-specific copy policy.</summary>
        public static bool GenerateAll()
        {
            if (!ValidateSettings()) return false;
            try
            {
                EditorUtility.DisplayProgressBar("HybridCLR", "Generate All...", 0.1f);
                PrebuildCommand.GenerateAll();
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[HybridCLR] Generate All failed: {exception.Message}");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static bool GenerateAllAndCopyDlls(HybridCLRBuildProfile profile,
            BuildTarget target)
        {
            if (!ValidateSettings()) return false;
            try
            {
                EditorUtility.DisplayProgressBar("HybridCLR", "Generate All...", 0.1f);
                PrebuildCommand.GenerateAll();

                EditorUtility.DisplayProgressBar("HybridCLR", "Copying DLLs...", 0.7f);
                if (!CopyHotUpdateDlls(profile, target)) return false;

                CopyCurrentAotMetadata(profile, target);
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[HybridCLR] Generate All failed: {exception.Message}");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static bool BackupAotDlls(HybridCLRBuildProfile profile, BuildTarget target,
            string platform, string version)
        {
            var sourceDirectory = GetAssembliesPostIl2CppStripDirectory(target);
            if (!Directory.Exists(sourceDirectory))
            {
                Debug.LogError($"[HybridCLR] Stripped AOT DLL directory does not exist: {sourceDirectory}");
                return false;
            }

            var backupDirectory = GetAotBackupDirectory(profile, target, platform, version);
            if (Directory.Exists(backupDirectory))
                Directory.Delete(backupDirectory, true);
            Directory.CreateDirectory(backupDirectory);

            var count = 0;
            foreach (var sourceFile in Directory.GetFiles(sourceDirectory, "*.dll"))
            {
                File.Copy(sourceFile,
                    Path.Combine(backupDirectory, Path.GetFileName(sourceFile)), true);
                count++;
            }

            if (File.Exists(profile.AotGenericReferencesPath))
            {
                File.Copy(profile.AotGenericReferencesPath,
                    Path.Combine(backupDirectory, "AOTGenericReferences.cs"), true);
            }

            Debug.Log($"[HybridCLR] Backed up {count} AOT DLLs to {backupDirectory}");
            return true;
        }

        public static string GetAotBackupDirectory(HybridCLRBuildProfile profile,
            BuildTarget target, string platform, string version) =>
            Path.Combine(profile.AotBackupRoot, target.ToString(), platform, version);

        public static string[] GetAvailableBackupVersions(HybridCLRBuildProfile profile,
            BuildTarget target, string platform, string archiveTargetRoot = null)
        {
            var versions = new HashSet<string>(StringComparer.Ordinal);
            AddChildDirectories(versions,
                Path.Combine(profile.AotBackupRoot, target.ToString(), platform));
            AddChildDirectories(versions, archiveTargetRoot);
            return versions
                .OrderByDescending(value => Version.TryParse(value, out var parsed)
                    ? parsed : new Version(0, 0))
                .ToArray();
        }

        public static bool CompileHotUpdateOnly(HybridCLRBuildProfile profile,
            BuildTarget target, string platform, string aotBackupVersion,
            bool development, bool strictMetadataCheck = false, bool stripAot = true)
        {
            if (!ValidateSettings()) return false;

            var backupDirectory = GetAotBackupDirectory(
                profile, target, platform, aotBackupVersion);
            if (!Directory.Exists(backupDirectory))
            {
                Debug.LogError($"[HybridCLR] AOT backup does not exist: {backupDirectory}");
                return false;
            }

            try
            {
                EditorUtility.DisplayProgressBar("HybridCLR Hot Update", "Compiling DLLs...", 0.15f);
                if (!CompileDlls(target, development)) return false;

                EditorUtility.DisplayProgressBar("HybridCLR Hot Update",
                    "Checking missing metadata...", 0.35f);
                var metadata = CheckMissingMetadata(target, backupDirectory);
                if (strictMetadataCheck && metadata.ShouldFailStrict)
                {
                    Debug.LogError("[HybridCLR] Strict metadata validation failed.");
                    return false;
                }

                EditorUtility.DisplayProgressBar("HybridCLR Hot Update", "Copying DLLs...", 0.6f);
                if (!CopyHotUpdateDlls(profile, target)) return false;

                CopyAotMetadataFrom(profile, backupDirectory, stripAot, backupDirectory);
                AssetDatabase.Refresh();
                return true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool CompileDlls(BuildTarget target, bool development)
        {
            try
            {
                CompileDllCommand.CompileDll(target, development);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[HybridCLR] DLL compilation failed: {exception.Message}");
                return false;
            }
        }

        private static bool CopyHotUpdateDlls(HybridCLRBuildProfile profile,
            BuildTarget target)
        {
            Directory.CreateDirectory(profile.HotUpdateDllDirectory);
            var sourceDirectory = GetHotUpdateDllOutputDirectory(target);
            var succeeded = true;
            foreach (var assemblyName in GetHotUpdateAssemblyNames())
            {
                var sourcePath = Path.Combine(sourceDirectory, assemblyName + ".dll");
                var destinationPath = Path.Combine(profile.HotUpdateDllDirectory,
                    assemblyName + ".dll.bytes");
                if (!File.Exists(sourcePath) || new FileInfo(sourcePath).Length == 0)
                {
                    Debug.LogError($"[HybridCLR] Hot-update DLL is missing or empty: {sourcePath}");
                    succeeded = false;
                    continue;
                }
                File.Copy(sourcePath, destinationPath, true);
            }
            return succeeded;
        }

        private static void CopyCurrentAotMetadata(HybridCLRBuildProfile profile,
            BuildTarget target)
        {
            var sourceDirectory = GetAssembliesPostIl2CppStripDirectory(target);
            if (!Directory.Exists(sourceDirectory))
            {
                Debug.LogWarning($"[HybridCLR] Stripped AOT DLL directory does not exist: {sourceDirectory}");
                return;
            }
            CopyAotMetadataFrom(profile, sourceDirectory, true, null);
        }

        private static void CopyAotMetadataFrom(HybridCLRBuildProfile profile,
            string sourceDirectory, bool strip, string backupDirectory)
        {
            Directory.CreateDirectory(profile.AotMetadataDirectory);
            foreach (var assemblyName in GetAotAssemblyNames(profile, backupDirectory))
            {
                var sourcePath = Path.Combine(sourceDirectory, assemblyName + ".dll");
                if (!File.Exists(sourcePath))
                {
                    Debug.LogWarning($"[HybridCLR] AOT DLL does not exist: {sourcePath}");
                    continue;
                }

                var destinationPath = Path.Combine(profile.AotMetadataDirectory,
                    assemblyName + ".dll.bytes");
                if (strip)
                    File.WriteAllBytes(destinationPath, StripAotAssembly(File.ReadAllBytes(sourcePath)));
                else
                    File.Copy(sourcePath, destinationPath, true);

                DeleteFileAndMeta(Path.Combine(profile.HotUpdateDllDirectory,
                    assemblyName + ".dll.bytes"));
            }
        }

        private static HybridCLRMetadataCheckResult CheckMissingMetadata(BuildTarget target,
            string aotDllDirectory)
        {
            var result = new HybridCLRMetadataCheckResult();
            var hotUpdateNames = GetHotUpdateAssemblyNames();
            var checker = new MissingMetadataChecker(aotDllDirectory, hotUpdateNames);

            var hotDllDirectory = GetHotUpdateDllOutputDirectory(target);
            foreach (var assemblyName in hotUpdateNames)
            {
                var path = Path.Combine(hotDllDirectory, assemblyName + ".dll");
                if (!File.Exists(path))
                {
                    result.FileNotFound = true;
                    continue;
                }

                void Handler(string message, string _, LogType __)
                {
                    if (string.IsNullOrEmpty(message)) return;
                    if (message.IndexOf("Missing Type", StringComparison.OrdinalIgnoreCase) >= 0)
                        result.HasTypeMissing = true;
                    else if (message.IndexOf("Missing Method", StringComparison.OrdinalIgnoreCase) >= 0)
                        result.HasMethodMissing = true;
                    else if (message.IndexOf("Missing AOT Assembly", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             message.IndexOf("Missing assembly", StringComparison.OrdinalIgnoreCase) >= 0)
                        result.HasAssemblyMissing = true;
                }

                Application.logMessageReceived += Handler;
                try { checker.Check(path); }
                finally { Application.logMessageReceived -= Handler; }
            }
            return result;
        }

        private static List<string> GetAotAssemblyNames(HybridCLRBuildProfile profile,
            string backupDirectory)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var referencePath = string.IsNullOrEmpty(backupDirectory)
                ? profile.AotGenericReferencesPath
                : Path.Combine(backupDirectory, "AOTGenericReferences.cs");
            var parsed = ParseAotAssemblyList(referencePath);
            if (parsed.Count == 0)
                parsed = SettingsUtil.AOTAssemblyNames;

            foreach (var name in parsed.Concat(profile.MandatoryAotAssemblies))
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                names.Add(name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(0, name.Length - 4) : name);
            }
            return names.ToList();
        }

        private static List<string> ParseAotAssemblyList(string path)
        {
            if (!File.Exists(path)) return new List<string>();
            var section = Regex.Match(File.ReadAllText(path),
                @"//\s*\{\{\s*AOT assemblies(.+?)//\s*\}\}", RegexOptions.Singleline);
            if (!section.Success) return new List<string>();
            return Regex.Matches(section.Groups[1].Value, @"""([^""]+\.dll)""")
                .Cast<Match>().Select(match => match.Groups[1].Value).ToList();
        }

        private static List<string> GetHotUpdateAssemblyNames() =>
            SettingsUtil.HotUpdateAssemblyNamesExcludePreserved;

        private static string GetHotUpdateDllOutputDirectory(BuildTarget target) =>
            SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);

        private static string GetAssembliesPostIl2CppStripDirectory(BuildTarget target) =>
            SettingsUtil.GetAssembliesPostIl2CppStripDir(target);

        private static byte[] StripAotAssembly(byte[] assemblyBytes) =>
            AOTAssemblyMetadataStripper.Strip(assemblyBytes);

        private static void AddChildDirectories(ISet<string> target, string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
            foreach (var directory in Directory.GetDirectories(root))
                target.Add(Path.GetFileName(directory));
        }

        private static void DeleteFileAndMeta(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
        }
    }

    public sealed class HybridCLRMetadataCheckResult
    {
        public bool HasTypeMissing { get; internal set; }
        public bool HasMethodMissing { get; internal set; }
        public bool HasAssemblyMissing { get; internal set; }
        public bool FileNotFound { get; internal set; }
        public bool ShouldFailStrict => HasTypeMissing || HasMethodMissing || FileNotFound;
    }
}
