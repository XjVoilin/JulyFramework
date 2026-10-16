using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace July.AssetCheck
{
    // 本地快照恢复为只读报告；只有重新扫描后的有效结果才允许修改资源。
    internal sealed class AssetCheckReportStore
    {
        internal const string RelativeDirectory = "Library/JulyAssetCheck/Reports";
        internal string DirectoryPath { get; }

        internal AssetCheckReportStore(string projectDirectory) =>
            DirectoryPath = Path.Combine(projectDirectory, RelativeDirectory);

        internal string PathFor(AssetCheckKind kind) => Path.Combine(DirectoryPath, kind + ".json");

        internal AssetCheckReport Load(AssetCheckKind kind)
        {
            var path = PathFor(kind);
            if (!File.Exists(path)) return null;
            AssetCheckReportSnapshot data;
            try { data = JsonUtility.FromJson<AssetCheckReportSnapshot>(File.ReadAllText(path)); }
            catch (ArgumentException exception) { throw new InvalidDataException("报告 JSON 无法解析：" + path, exception); }
            if (data == null || (data.schemaVersion != 1 && data.schemaVersion != 2) || data.module != kind.ToString() ||
                !Enum.TryParse(data.platform, out AssetCheckPlatform platform) || !Enum.IsDefined(typeof(AssetCheckPlatform), platform) ||
                string.IsNullOrEmpty(data.standardsContext) || data.issues == null ||
                !new[] { "Completed", "Cancelled", "Failed", "Scanning", "Fixing" }.Contains(data.status) ||
                !DateTime.TryParseExact(data.startedAtUtc, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started) ||
                !DateTime.TryParseExact(data.savedAtUtc, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var saved))
                throw new InvalidDataException("报告版本、模块、时间或内容无效：" + path);
            if (AssetCheckCatalog.Entries.Single(entry => entry.Kind == kind).ProjectWide)
            { if (data.scope != "Project") throw new InvalidDataException("全项目报告范围无效。"); }
            else AssetCheckSettings.ValidateScope(data.scope);
            if (data.totalFiles < 0 || data.checkedFiles < 0 || data.ignoredFiles < 0 ||
                (long)data.checkedFiles + data.ignoredFiles > data.totalFiles)
                throw new InvalidDataException("报告文件统计无效：" + path);
            if ((data.status == "Completed" && !data.complete) || (data.status == "Cancelled" && !data.cancelled) ||
                (data.status == "Failed" && string.IsNullOrEmpty(data.failure)))
                throw new InvalidDataException("报告状态与完成标记不一致：" + path);
            var report = new AssetCheckReport(kind, data.standardsContext, platform, data.scope, started.ToLocalTime())
            {
                IsSnapshot = true, SnapshotCheckedFiles = data.checkedFiles, SnapshotIgnoredFiles = data.ignoredFiles,
                TotalFiles = data.totalFiles, Complete = data.complete, Cancelled = data.cancelled, Stale = data.stale,
                Failure = string.IsNullOrEmpty(data.failure) ? null : data.failure
            };
            foreach (var row in data.issues)
            {
                if (row == null || string.IsNullOrEmpty(row.ruleId) || row.resourceName == null || row.actual == null ||
                    row.expected == null || row.problem == null || row.targets == null ||
                    (!row.hasProblem && !row.unverified) || string.IsNullOrEmpty(row.targetId) ||
                    !(AssetCheckTarget.ProjectFiles.Contains(row.targetId) || Regex.IsMatch(row.targetId, "^[a-fA-F0-9]{32}$")) ||
                    string.IsNullOrEmpty(row.assetPath) || row.assetPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                    !(row.assetPath.StartsWith("Assets/", StringComparison.Ordinal) || AssetCheckTarget.ProjectFiles.Contains(row.assetPath)) ||
                    !long.TryParse(row.localId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var localId) ||
                    (row.unverifiedTargets != null && row.unverifiedTargets.Except(row.targets).Any()))
                    throw new InvalidDataException("报告问题行无效：" + path);
                report.Issues.Add(new AssetCheckIssue(row.assetPath, row.targetId, kind, row.ruleId, row.problem, row.actual,
                    row.expected, row.canFix, row.unverified, row.resourceName, localId, row.targets, row.location ?? "",
                    row.hasProblem, row.propertyPath, row.unverifiedTargets));
            }
            if (report.PassedAssets != data.passedFiles || report.PassedAssets < 0 || report.ProblemAssets != data.problemFiles ||
                report.UnverifiedAssets != data.unverifiedFiles || report.Issues.Count(row => row.HasProblem) != data.problemItems ||
                report.Issues.Count(row => row.Unverified) != data.unverifiedItems)
                throw new InvalidDataException("报告问题与统计不一致：" + path);
            return report;
        }

        internal void Save(AssetCheckReport report, bool fixing)
        {
            var snapshot = new AssetCheckReportSnapshot
            {
                schemaVersion = 2,
                module = report.Kind.ToString(), moduleName = AssetCheckWorkspace.KindName(report.Kind),
                platform = report.Platform.ToString(), scope = report.Scope, standardsContext = report.Context,
                startedAtUtc = report.StartedAt.ToUniversalTime().ToString("O"), savedAtUtc = DateTime.UtcNow.ToString("O"),
                status = report.Failure != null ? "Failed" : report.Cancelled ? "Cancelled" : fixing ? "Fixing" :
                    report.Complete ? "Completed" : "Scanning",
                complete = report.Complete, cancelled = report.Cancelled, stale = report.Stale, failure = report.Failure,
                totalFiles = report.TotalFiles, checkedFiles = report.CheckedFiles,
                passedFiles = report.PassedAssets, problemFiles = report.ProblemAssets,
                problemItems = report.Issues.Count(issue => issue.HasProblem),
                unverifiedFiles = report.UnverifiedAssets, unverifiedItems = report.Issues.Count(issue => issue.Unverified),
                ignoredFiles = report.IgnoredFiles,
                issues = report.Issues.Select(issue => new AssetCheckIssueSnapshot
                {
                    assetPath = issue.AssetPath, targetId = issue.TargetId, ruleId = issue.RuleId,
                    resourceName = issue.ResourceName, problem = issue.Problem, actual = issue.Actual, expected = issue.Expected,
                    canFix = issue.CanFix, hasProblem = issue.HasProblem, unverified = issue.Unverified,
                    localId = issue.LocalId.ToString(CultureInfo.InvariantCulture), targets = issue.Targets.ToArray(),
                    location = issue.Location, propertyPath = issue.PropertyPath, unverifiedTargets = issue.UnverifiedTargets.ToArray()
                }).ToArray()
            };
            Directory.CreateDirectory(DirectoryPath);
            var path = PathFor(report.Kind);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(snapshot, true) + Environment.NewLine, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    [Serializable]
    internal sealed class AssetCheckReportSnapshot
    {
        public int schemaVersion;
        public string module, moduleName, platform, scope, standardsContext, startedAtUtc, savedAtUtc, status, failure;
        public bool complete, cancelled, stale;
        public int totalFiles, checkedFiles, passedFiles, problemFiles, problemItems, unverifiedFiles, unverifiedItems, ignoredFiles;
        public AssetCheckIssueSnapshot[] issues;
    }

    [Serializable]
    internal sealed class AssetCheckIssueSnapshot
    {
        public string assetPath, targetId, ruleId, resourceName, problem, actual, expected, localId, location, propertyPath;
        public bool canFix, hasProblem, unverified;
        public string[] targets, unverifiedTargets;
    }
}
