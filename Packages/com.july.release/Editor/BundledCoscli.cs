using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor.PackageManager;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>随 Release 包分发的工具；项目仅提供凭证，不查找项目或 PATH 中的旧程序。</summary>
    public static class BundledCoscli
    {
        public const string Version = "1.0.8";

        public static string GetExecutablePath()
        {
            var package = PackageInfo.FindForAssembly(typeof(BundledCoscli).Assembly);
            return Path.GetFullPath(Path.Combine(package.resolvedPath, "Tools~", "coscli",
                RelativeExecutablePath(Application.platform, RuntimeInformation.OSArchitecture)));
        }

        internal static string RelativeExecutablePath(RuntimePlatform platform, Architecture architecture)
        {
            if (platform == RuntimePlatform.WindowsEditor && architecture == Architecture.X64)
                return "windows-x64/coscli.exe";
            if (platform == RuntimePlatform.OSXEditor && architecture == Architecture.Arm64)
                return "macos-arm64/coscli";
            throw new PlatformNotSupportedException(
                $"Release 内置 COSCLI 尚不支持当前编辑器主机 {platform}/{architecture}；目前提供 Windows x64、macOS ARM64。");
        }

        internal static string PrepareExecutable(string packagedExecutable)
        {
#if UNITY_EDITOR_OSX
            // Git/UPM on Windows may not preserve the executable bit. Keep the package read-only.
            var directory = Path.GetFullPath(Path.Combine("Library", "July.Release", "Tools", "coscli", Version));
            Directory.CreateDirectory(directory);
            var executable = Path.Combine(directory, "coscli");
            File.Copy(packagedExecutable, executable, true);
            var start = new ProcessStartInfo("chmod", "+x coscli")
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true
            };
            using var process = Process.Start(start);
            process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException($"无法设置 COSCLI 工作副本的执行权限：{executable}");
            return executable;
#else
            return packagedExecutable;
#endif
        }
    }
}
