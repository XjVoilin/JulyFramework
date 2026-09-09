using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using YooAsset.Editor;

namespace July.Release.Editor
{
    /// <summary>
    /// 构建流水线共享工具方法。不含 UI 逻辑，可被步骤和 CI 入口安全调用。
    /// </summary>
    public static class BuildUtils
    {
        public static string CdnRoot => ReleaseProject.Profile.CdnRoot;
        public const string QAPlanVersion = "99.99.99";
        public static string PackageName => ReleaseProject.Profile.PackageName;
        public static string BootConfigPath => ReleaseProject.Profile.BootConfigPath;
        public static string BuildConfigPath => ReleaseProject.Profile.BuildConfigPath;
        public static readonly string YooAssetPipelineType = nameof(EBuildPipeline.ScriptableBuildPipeline);

        /// <summary>
        /// AOT 归档根目录：{项目根}/../AOTBackup/{项目文件夹名}/
        /// </summary>
        private static string AOTArchiveRoot => Path.GetFullPath(ReleaseProject.Profile.AotArchiveRoot);

        public static string GetAOTArchiveDir(BuildTarget target, string platform, string version)
        {
            return Path.Combine(AOTArchiveRoot, target.ToString(), platform, version);
        }

        public static string GetAOTArchiveTargetRoot(BuildTarget target, string platform)
        {
            return Path.Combine(AOTArchiveRoot, target.ToString(), platform);
        }

        public static string GetCdnPath(string env, string platform, string coreVersion, string planVersion)
        {
            return Path.GetFullPath($"{CdnRoot}/{env}/{platform}/{coreVersion}/{planVersion}");
        }

        public static string NewPackageVersion()
        {
            return DateTime.Now.ToString("yyyyMMddHHmmss");
        }

        public static bool IsValidVersion(string version)
        {
            return Version.TryParse(version, out _);
        }

        public static bool IsVersionGreaterOrEqual(string a, string b)
        {
            if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
                return va >= vb;
            return false;
        }

        public static (string bucket, string region, string pathPrefix) ParseCloudInfo(string cloudUrl)
        {
            if (!TryParseResourceRoot(cloudUrl, out var uri))
                return (null, null, null);

            var match = Regex.Match(uri.Host, @"^([^.]+)\.cos\.([^.]+)\.myqcloud\.com$");
            if (!match.Success)
                return (null, null, null);

            return (match.Groups[1].Value, match.Groups[2].Value, uri.AbsolutePath.Trim('/'));
        }

        static bool TryParseResourceRoot(string url, out Uri uri)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                   && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
        }

        /// <summary>校验下载与上传的项目资源根 URL，COS 对象路径区分大小写。</summary>
        public static string ValidateResourceUrls(BuildContext ctx,
            out string bucket, out string region, out string pathPrefix)
        {
            (bucket, region, pathPrefix) = ParseCloudInfo(ctx.CloudUrl);
            if (bucket == null)
                return $"无法从 BuildConfig.cloudUrl 解析 COS 项目资源根 URL:\n{ctx.CloudUrl}\n格式: https://<bucket>.cos.<region>.myqcloud.com/<项目路径>（不能包含查询参数或片段）";

            if (!TryParseResourceRoot(ctx.CdnUrl, out var cdnUri))
                return $"BootConfig.cdnUrl 必须是 HTTP(S) 项目资源根 URL，且不能包含查询参数或片段:\n{ctx.CdnUrl}";

            var cdnPathPrefix = cdnUri.AbsolutePath.Trim('/');
            if (!string.Equals(cdnPathPrefix, pathPrefix, StringComparison.Ordinal))
                return $"项目资源路径前缀不一致，下载目录与上传目录必须相同:\nBootConfig.cdnUrl: {ctx.CdnUrl}（前缀: {cdnPathPrefix}）\nBuildConfig.cloudUrl: {ctx.CloudUrl}（前缀: {pathPrefix}）";

            return null;
        }

        /// <summary>以已解析的项目路径前缀拼接 COS 对象 URL，不改变本地产物目录。</summary>
        public static string BuildCosObjectUrl(string bucket, string pathPrefix, params string[] paths)
        {
            var parts = new List<string> { bucket };
            if (pathPrefix.Length > 0)
                parts.Add(pathPrefix.Trim('/'));
            foreach (var path in paths)
                parts.Add(path.Trim('/'));
            return "cos://" + string.Join("/", parts);
        }

        public static void CopyDirectory(string srcDir, string dstDir)
        {
            var tmpDir = dstDir + "_tmp_" + Path.GetRandomFileName();
            try
            {
                Directory.CreateDirectory(tmpDir);

                foreach (var file in Directory.GetFiles(srcDir))
                    File.Copy(file, Path.Combine(tmpDir, Path.GetFileName(file)));

                foreach (var dir in Directory.GetDirectories(srcDir))
                    CopyDirectory(dir, Path.Combine(tmpDir, Path.GetFileName(dir)));

                if (Directory.Exists(dstDir))
                    Directory.Delete(dstDir, true);

                Directory.Move(tmpDir, dstDir);
            }
            catch
            {
                if (Directory.Exists(tmpDir))
                    Directory.Delete(tmpDir, true);
                throw;
            }
        }

        public static Dictionary<string, long> SnapshotDirectory(string dir)
        {
            var snapshot = new Dictionary<string, long>();
            if (!Directory.Exists(dir)) return snapshot;

            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                var relativePath = file.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                snapshot[relativePath] = new FileInfo(file).Length;
            }

            return snapshot;
        }

        public static string FormatSize(long bytes)
        {
            var abs = Math.Abs(bytes);
            if (abs < 1024) return $"{bytes} B";
            if (abs < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F2} MB";
        }

        public static string GetCoscliPath() => Path.GetFullPath(ReleaseProject.Profile.CoscliPath);
        public static string GetCoscliConfigPath() => Path.GetFullPath(ReleaseProject.Profile.CoscliConfigPath);

        /// <summary>
        /// 校验 COS 上传所需的环境（项目资源根 URL、凭证、coscli 工具）。
        /// 返回 null 表示通过；非 null 为错误描述。
        /// </summary>
        public static string ValidateCosEnvironment(BuildContext ctx,
            out string bucket, out string region, out string pathPrefix, out string coscliPath)
        {
            coscliPath = null;
            var urlError = ValidateResourceUrls(ctx, out bucket, out region, out pathPrefix);
            if (urlError != null) return urlError;

            coscliPath = GetCoscliPath();
            if (!File.Exists(coscliPath))
                return $"coscli 未找到: {coscliPath}";

            var configPath = GetCoscliConfigPath();
            return !File.Exists(configPath) ? $"coscli 配置未找到: {configPath}，请确认已提交到 git" : null;
        }

        /// <summary>
        /// 执行 coscli 命令并返回结果。自动通过 -c 指定项目内配置文件 Tools/coscli/.cos.yaml。
        /// macOS/Linux 下自动确保可执行权限（Unity 操作可能重置文件权限）。
        /// </summary>
        public static (int exitCode, string stdout, string stderr) RunCoscli(
            string coscliPath, string args)
        {
            EnsureExecutable(coscliPath);
            var configPath = GetCoscliConfigPath();
            var fullArgs = $"-c \"{configPath}\" {args}";

            var psi = new ProcessStartInfo
            {
                FileName = coscliPath,
                Arguments = fullArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return (-1, "", "Process.Start returned null");

            proc.StandardInput.Close();
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode, stdout, stderr);
        }

        /// <summary>macOS/Linux: 确保文件有可执行权限。Windows 上无操作。</summary>
        private static void EnsureExecutable(string path)
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch { /* 权限修复失败不阻断，后续 Process.Start 会报原始错误 */ }
#endif
        }
    }
}
