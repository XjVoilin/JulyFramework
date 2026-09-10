using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using July.Release.Editor;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace July.Release.Tests
{
    public sealed class BundledCoscliTests
    {
        [TestCase(RuntimePlatform.WindowsEditor, Architecture.X64, "windows-x64/coscli.exe")]
        [TestCase(RuntimePlatform.OSXEditor, Architecture.Arm64, "macos-arm64/coscli")]
        public void SelectsBundledHostTool(RuntimePlatform platform, Architecture architecture, string expected)
            => Assert.AreEqual(expected, BundledCoscli.RelativeExecutablePath(platform, architecture));

        [TestCase(RuntimePlatform.LinuxEditor, Architecture.X64)]
        [TestCase(RuntimePlatform.OSXEditor, Architecture.X64)]
        public void UnsupportedHostFailsExplicitly(RuntimePlatform platform, Architecture architecture)
            => Assert.Throws<PlatformNotSupportedException>(() => BundledCoscli.RelativeExecutablePath(platform, architecture));

        static string ToolRoot => Path.Combine(PackageInfo.FindForAssembly(typeof(BundledCoscli).Assembly).resolvedPath, "Tools~", "coscli");

        [Test] public void ResolvesInstalledPackageAndKeepsProjectCredentials()
        {
            var executable = BundledCoscli.GetExecutablePath();
            Assert.AreEqual(Path.GetFullPath(Path.Combine(ToolRoot,
                BundledCoscli.RelativeExecutablePath(Application.platform, RuntimeInformation.OSArchitecture))), executable);
            Assert.IsTrue(File.Exists(executable));
            Assert.AreEqual(Path.GetFullPath("Tools/coscli/.cos.yaml"), BuildUtils.GetCoscliConfigPath());
        }

        [TestCase("windows-x64/coscli.exe", "74f3b5ebbe89be2c013f9d3b2d8968691372801f2671bb31548eab4476fe9179")]
        [TestCase("macos-arm64/coscli", "df0018fbf78b552cbe875ebe26e8bdf7938c7f4394959f913dfc2ea4d1252568")]
        public void BinaryMatchesPinnedOfficialRelease(string relative, string expected)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(Path.Combine(ToolRoot, relative));
            Assert.AreEqual(expected, BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant());
        }

        [Test] public void BundledExecutableReportsExpectedVersionWithoutCredentials()
        {
            var directory = Path.Combine(Path.GetTempPath(), "July Coscli Test " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var start = new ProcessStartInfo(BundledCoscli.PrepareExecutable(BundledCoscli.GetExecutablePath()), "--version")
                {
                    WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var process = Process.Start(start);
                if (!process.WaitForExit(15000))
                {
                    process.Kill();
                    Assert.Fail("COSCLI --version timed out");
                }
                Assert.AreEqual(0, process.ExitCode);
                StringAssert.Contains("v" + BundledCoscli.Version, process.StandardOutput.ReadToEnd());
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
