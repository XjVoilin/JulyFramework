using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Debug = UnityEngine.Debug;

namespace July.Release.Editor
{
    /// <summary>
    /// Jenkins 流水线末尾：
    ///   1) 根据 ctx 构造 annotated tag 名和 message
    ///   2) git tag -a <name> -m "<message>"（当前 HEAD）
    ///   3) git push origin <name>（禁用 --force）
    /// 主名已存在 → FullBuild 使用 +full-b{BUILD_NUMBER}，
    /// HotUpdate 使用 +hot-b{BUILD_NUMBER}；保留所有历史。
    /// Validate 会在整条流水线执行前锁定并检查最终 tag，避免上传后才发现冲突。
    /// 仅在 Jenkins 环境运行（靠 BUILD_NUMBER 环境变量识别；本地执行时跳过）。
    /// </summary>
    public sealed class GitTagStep : BuildStep
    {
        private string _plannedTagName;
        private string _plannedBuildType;

        public override string Name => "发布归档 Git Tag";

        public override string Validate(BuildContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.CoreVersion)) return "ctx.CoreVersion 为空";
            if (string.IsNullOrEmpty(ctx.PlanVersion)) return "ctx.PlanVersion 为空";
            if (string.IsNullOrEmpty(ctx.Platform))   return "ctx.Platform 为空";
            if (string.IsNullOrEmpty(ctx.Env))        return "ctx.Env 为空";

            _plannedTagName = null;
            _plannedBuildType = null;

            if (ctx.IsQABuild) return null;

            var buildNumber = Environment.GetEnvironmentVariable("BUILD_NUMBER");
            if (string.IsNullOrEmpty(buildNumber)) return null;

            _plannedBuildType = ResolveBuildType(ctx);
            if (!TryResolveTagName(ctx, buildNumber, _plannedBuildType,
                    out _plannedTagName, out var error))
                return error;

            Debug.Log($"[GitTag] 预校验通过: {_plannedTagName} ({_plannedBuildType})");
            return null;
        }

        public override bool Execute(BuildContext ctx)
        {
            if (ctx.IsQABuild)
            {
                Debug.Log("[GitTag] QA 测试构建，跳过 tag 推送。");
                return true;
            }

            var buildNumber = Environment.GetEnvironmentVariable("BUILD_NUMBER");
            if (string.IsNullOrEmpty(buildNumber))
            {
                Debug.Log("[GitTag] 检测到非 Jenkins 环境（无 BUILD_NUMBER 变量），跳过 tag 推送。");
                return true;
            }

            var buildType = _plannedBuildType ?? ResolveBuildType(ctx);
            var tagName = _plannedTagName;
            if (string.IsNullOrEmpty(tagName) &&
                !TryResolveTagName(ctx, buildNumber, buildType, out tagName, out var resolveError))
            {
                Debug.LogError($"[GitTag] 无法确定归档标签: {resolveError}");
                return false;
            }

            if (!TryTagExists(tagName, out var tagExists, out var checkError))
            {
                Debug.LogError($"[GitTag] 执行前复查失败: {checkError}");
                return false;
            }
            if (tagExists)
            {
                if (ctx.ForceRebuild)
                {
                    Debug.Log($"[GitTag] 强制重建，保留已有归档标签，跳过重复创建和推送: {tagName}");
                    return true;
                }
                Debug.LogError($"[GitTag] 执行前复查发现标签已存在: {tagName}");
                return false;
            }

            var message = BuildAnnotatedMessage(ctx, buildNumber, buildType);

            if (!RunGit(new[] { "tag", "-a", tagName, "-m", message }, out var tagStdout, out var tagStderr))
            {
                Debug.LogError($"[GitTag] git tag -a 失败:\n{tagStdout}\n{tagStderr}");
                return false;
            }

            if (!RunGit(new[] { "push", "origin", tagName }, out var pushStdout, out var pushStderr))
            {
                Debug.LogError($"[GitTag] git push 失败 (tag={tagName}):\n{pushStdout}\n{pushStderr}");
                if (!RunGit(new[] { "tag", "-d", tagName }, out _, out var deleteStderr))
                    Debug.LogError($"[GitTag] 清理本地失败标签时出错: {deleteStderr}");
                return false;
            }

            Debug.Log($"[GitTag] 已打并推送: {tagName}\n{message}");
            return true;
        }

        private static string ResolveBuildType(BuildContext ctx) =>
            string.IsNullOrEmpty(ctx.AOTBackupVersion) ? "FullBuild" : "HotUpdate";

        private static bool TryResolveTagName(BuildContext ctx, string buildNumber, string buildType,
            out string tagName, out string error)
        {
            tagName = null;
            error = null;

            var platformLower = ctx.Platform.ToLowerInvariant();
            var envLower = ctx.Env.ToLowerInvariant();
            var mainName = $"release/{envLower}/{platformLower}/{ctx.CoreVersion}/{ctx.PlanVersion}";

            if (!TryTagExists(mainName, out var mainExists, out error)) return false;
            if (!mainExists)
            {
                tagName = mainName;
                return true;
            }

            var buildKind = buildType == "HotUpdate" ? "hot" : "full";
            tagName = $"{mainName}+{buildKind}-b{buildNumber}";
            if (!TryTagExists(tagName, out var archiveExists, out error)) return false;
            if (!archiveExists || ctx.ForceRebuild) return true;

            error = $"归档标签已存在: {tagName}。请使用新的 Jenkins 构建号重试";
            return false;
        }

        private static string BuildAnnotatedMessage(
            BuildContext ctx, string buildNumber, string buildType)
        {
            var jenkinsUrl = Environment.GetEnvironmentVariable("BUILD_URL") ?? "";
            var gitCommit = Environment.GetEnvironmentVariable("GIT_COMMIT") ?? "<unknown>";
            var buildTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
            var cdnUrl = string.IsNullOrEmpty(ctx.CloudUrl)
                ? "<unknown>"
                : $"{ctx.CloudUrl.TrimEnd('/')}/{ctx.Env}/{ctx.Platform}/{ctx.CoreVersion}/{ctx.PlanVersion}/";

            var sb = new StringBuilder();
            sb.AppendLine($"BuildType:        {buildType}");
            sb.AppendLine($"Platform:         {ctx.Platform}");
            sb.AppendLine($"CoreVersion:      {ctx.CoreVersion}");
            sb.AppendLine($"PlanVersion:      {ctx.PlanVersion}");
            sb.AppendLine($"AOTBackupVersion: {ctx.AOTBackupVersion ?? ctx.CoreVersion}");
            sb.AppendLine($"Jenkins:          {jenkinsUrl}");
            sb.AppendLine($"CDN:              {cdnUrl}");
            sb.AppendLine($"BuildTime:        {buildTime}");
            sb.AppendLine($"Commit:           {gitCommit}");
            sb.AppendLine($"BuildNumber:      {buildNumber}");
            return sb.ToString();
        }

        private static bool TryTagExists(string tagName, out bool exists, out string error)
        {
            if (!RunGit(new[] { "tag", "-l", tagName }, out var stdout, out var stderr))
            {
                exists = false;
                error = $"无法检查 Git 标签 {tagName}: {stderr}";
                return false;
            }

            exists = !string.IsNullOrWhiteSpace(stdout);
            error = null;
            return true;
        }

        /// <summary>
        /// 按数组传参，不经过 shell，避免 message 里的换行/引号被二次解释。
        /// </summary>
        private static bool RunGit(string[] args, out string stdout, out string stderr)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                stdout = stderr = "";
                return false;
            }

            stdout = proc.StandardOutput.ReadToEnd();
            stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
    }
}
