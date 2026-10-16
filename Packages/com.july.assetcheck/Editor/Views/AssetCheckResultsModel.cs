using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using UnityEditor;

namespace July.AssetCheck
{
    internal enum AssetCheckSort { FileName, Problem, Actual, Expected }

    internal sealed class AssetCheckFileRow
    {
        internal string Id => AllIssues[0].TargetId;
        internal string Path => AllIssues[0].AssetPath;
        internal string Name => System.IO.Path.GetFileName(Path);
        internal readonly AssetCheckIssue[] AllIssues, VisibleIssues;
        internal bool HasProblem => AllIssues.Any(issue => issue.HasProblem);
        internal bool HasUnverified => AllIssues.Any(issue => issue.Unverified);
        internal string DirectoryLabel => System.IO.Path.GetDirectoryName(Path).Replace('\\', '/');
        internal AssetCheckFileRow(AssetCheckIssue[] issues, string rule)
        {
            AllIssues = issues;
            VisibleIssues = issues.Where(issue => rule == null || issue.RuleId == rule).ToArray();
        }
    }

    internal sealed class AssetCheckDisplayRow
    {
        internal AssetCheckFileRow File;
        internal AssetCheckIssue Issue;
        internal bool UnknownHeader;
        internal bool IsFile => File != null && Issue == null;
        internal IEnumerable<AssetCheckIssue> Issues => Issue != null ? new[] { Issue } : File.VisibleIssues;
    }

    // 视图状态使用资源标识保存，不依赖排序或筛选后的列表索引。
    internal sealed class AssetCheckResultsModel
    {
        private AssetCheckReport _report;
        private int _revision = -1;
        internal string Rule { get; private set; }
        internal AssetCheckSort Sort = AssetCheckSort.FileName;
        internal bool Descending, ShowUnknown;
        internal readonly HashSet<string> Selected = new HashSet<string>();
        internal readonly HashSet<string> Expanded = new HashSet<string>();
        internal List<AssetCheckFileRow> Files { get; private set; } = new List<AssetCheckFileRow>();
        internal List<AssetCheckDisplayRow> Rows { get; private set; } = new List<AssetCheckDisplayRow>();
        internal int UnknownFiles => Files.Count(file => !file.HasProblem);
        internal int TotalAttentionFiles => _report?.AttentionFiles ?? 0;
        internal bool CanModify => _report != null && !_report.IsSnapshot && _report.Complete && !_report.Stale &&
            !_report.Cancelled && _report.Failure == null;
        internal string FilterDescription => $"当前筛选 {Files.Count} / 全部 {TotalAttentionFiles} 个需处理文件（含折叠项）";

        internal bool Bind(AssetCheckReport report)
        {
            var changed = !ReferenceEquals(_report, report) || _revision != (report?.Revision ?? -1);
            if (_report != null && report != null && _report.Kind != report.Kind) Rule = null;
            _report = report;
            if (changed)
            {
                _revision = report?.Revision ?? -1;
                Selected.Clear();
                Rebuild();
            }
            return changed;
        }

        internal void Filter(string rule)
        {
            Rule = rule;
            Selected.Clear();
            Rebuild();
        }

        internal void Rebuild()
        {
            Files = (_report?.Issues ?? new List<AssetCheckIssue>()).GroupBy(issue => issue.TargetId)
                .Select(group => new AssetCheckFileRow(group.ToArray(), Rule)).Where(file => file.VisibleIssues.Length > 0).ToList();
            Files.Sort(Compare);
            RebuildRows();
        }

        internal void SortBy(AssetCheckSort column)
        {
            Descending = Sort == column && !Descending;
            Sort = column;
            Rebuild();
        }

        internal void RebuildRows()
        {
            Rows = new List<AssetCheckDisplayRow>();
            foreach (var file in Files.Where(file => file.HasProblem)) AddFile(file);
            if (UnknownFiles > 0)
            {
                Rows.Add(new AssetCheckDisplayRow { UnknownHeader = true });
                if (ShowUnknown) foreach (var file in Files.Where(file => !file.HasProblem)) AddFile(file);
            }
        }

        private void AddFile(AssetCheckFileRow file)
        {
            Rows.Add(new AssetCheckDisplayRow { File = file });
            if (file.VisibleIssues.Length > 1 && Expanded.Contains(file.Id))
                foreach (var issue in file.VisibleIssues.OrderBy(issue => issue, Comparer<AssetCheckIssue>.Create(CompareIssues)))
                    Rows.Add(new AssetCheckDisplayRow { File = file, Issue = issue });
        }

        internal void Toggle(string id)
        {
            if (!Expanded.Remove(id)) Expanded.Add(id);
            RebuildRows();
        }

        internal void ExpandAll(bool value)
        {
            Expanded.Clear();
            ShowUnknown = value;
            if (value) Expanded.UnionWith(Files.Select(file => file.Id));
            RebuildRows();
        }

        internal AssetCheckIssue[] Fixable(bool selectedOnly) => Files
            .Where(file => (file.HasProblem || ShowUnknown) && (!selectedOnly || Selected.Contains(file.Id)))
            .SelectMany(file => file.VisibleIssues).Select(issue => issue.ConfirmedFix()).Where(issue => issue != null).ToArray();

        internal AssetCheckIssue[] AllFixable() => (_report?.Issues ?? new List<AssetCheckIssue>())
            .Select(issue => issue.ConfirmedFix()).Where(issue => issue != null).ToArray();

        internal static string Subject(AssetCheckIssue issue)
        {
            var suffix = issue.ResourceName.StartsWith(issue.AssetPath, StringComparison.Ordinal)
                ? issue.ResourceName.Substring(issue.AssetPath.Length).TrimStart(' ', '/') : issue.ResourceName;
            if (string.IsNullOrEmpty(suffix)) return "资源设置";
            var parts = suffix.Split('/').Select(part => part.Trim()).ToArray();
            return string.Join(" / ", parts.Skip(Math.Max(0, parts.Length - 3)));
        }

        private int Compare(AssetCheckFileRow left, AssetCheckFileRow right)
        {
            var order = Sort == AssetCheckSort.FileName ? EditorUtility.NaturalCompare(left.Name, right.Name) :
                CompareValue(GroupValue(left), GroupValue(right));
            if (order == 0) order = StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
            return Descending ? -order : order;
        }

        // 保持同一文件的问题分组；多问题文件使用当前排序列的最小值比较。
        private string GroupValue(AssetCheckFileRow file) => file.VisibleIssues.Select(SortValue)
            .OrderBy(value => value, Comparer<string>.Create(CompareValue)).First();

        private string SortValue(AssetCheckIssue issue) => Sort == AssetCheckSort.Problem ? issue.Problem :
            Sort == AssetCheckSort.Actual ? issue.Actual : Sort == AssetCheckSort.Expected ? issue.Expected : Subject(issue);

        private int CompareIssues(AssetCheckIssue left, AssetCheckIssue right)
        {
            var order = CompareValue(SortValue(left), SortValue(right));
            if (order == 0) order = StringComparer.Ordinal.Compare(left.RuleId, right.RuleId);
            if (order == 0) order = StringComparer.Ordinal.Compare(left.ResourceName, right.ResourceName);
            return Descending ? -order : order;
        }

        private static int CompareValue(string left, string right)
        {
            var leftNumber = decimal.TryParse(left.TrimStart('≤', '≥', ' '), NumberStyles.Number, CultureInfo.InvariantCulture, out var a);
            var rightNumber = decimal.TryParse(right.TrimStart('≤', '≥', ' '), NumberStyles.Number, CultureInfo.InvariantCulture, out var b);
            if (leftNumber && rightNumber) return a.CompareTo(b);
            if (leftNumber != rightNumber) return leftNumber ? -1 : 1;
            return EditorUtility.NaturalCompare(left, right);
        }
    }
}
