using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using UnityEditor;

namespace July.AssetCheck
{
    internal enum AssetCheckSort { FileName, Problem, Actual, Expected, Size }

    // 展示输入不拥有扫描生命周期；身份不再从第一条问题推导。
    internal sealed class AssetCheckFileData
    {
        internal string Id, Path, Status, Note;
        internal AssetCheckKind? Kind;
        internal AssetCheckIssue[] Issues = Array.Empty<AssetCheckIssue>();
        internal bool Complete, Unverified, Candidate;
        internal Func<string> ResolvePath;
        internal long? SizeBytes;
        internal bool SizePartial;
        internal string SizeNote;
    }

    internal sealed class AssetCheckFileRow
    {
        internal readonly AssetCheckFileData Data;
        internal string Id => Data.Id;
        internal string Path => Data.Path;
        internal string Name => System.IO.Path.GetFileName(Path);
        internal readonly AssetCheckIssue[] AllIssues, VisibleIssues;
        internal bool HasProblem => AllIssues.Any(issue => issue.HasProblem);
        internal bool HasUnverified => Data.Unverified || AllIssues.Any(issue => issue.Unverified);
        internal string DirectoryLabel => (System.IO.Path.GetDirectoryName(Path) ?? "").Replace('\\', '/');
        internal AssetCheckFileRow(AssetCheckFileData data, string rule)
        {
            Data = data; AllIssues = data.Issues;
            VisibleIssues = AllIssues.Where(issue => rule == null || issue.RuleId == rule).ToArray();
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
        private object _source, _category;
        private string _search = "", _status = "全部";
        private AssetCheckFileData[] _entries = Array.Empty<AssetCheckFileData>();
        internal string Rule { get; private set; }
        internal AssetCheckSort Sort = AssetCheckSort.FileName;
        internal bool Descending, ShowUnknown;
        internal readonly HashSet<string> Selected = new HashSet<string>();
        internal readonly HashSet<string> Expanded = new HashSet<string>();
        internal List<AssetCheckFileRow> Files { get; private set; } = new List<AssetCheckFileRow>();
        internal List<AssetCheckDisplayRow> Rows { get; private set; } = new List<AssetCheckDisplayRow>();
        internal IEnumerable<AssetCheckIssue> AllIssues => _entries.SelectMany(entry => entry.Issues);
        internal int UnknownFiles => Files.Count(file => !file.HasProblem && file.HasUnverified);
        internal int TotalAttentionFiles => _entries.Length;
        internal bool CanModify => _report != null && !_report.IsSnapshot && _report.Complete && !_report.Stale &&
            !_report.Cancelled && _report.Failure == null;
        internal string FilterDescription => $"当前筛选 {Files.Count} / 全部 {TotalAttentionFiles} 个文件（含折叠项）";

        internal bool Bind(AssetCheckReport report)
        {
            _report = report;
            return BindCore(report, report?.Kind, report?.Revision ?? -1, () =>
                (report?.Issues ?? new List<AssetCheckIssue>()).GroupBy(issue => issue.TargetId).Select(group =>
                {
                    var first = group.First();
                    return new AssetCheckFileData { Id = first.TargetId, Path = first.AssetPath, Kind = first.Kind,
                        Issues = group.ToArray(), Complete = report.Complete,
                        ResolvePath = () => AssetCheckTarget.Resolve(first.TargetId) };
                }).ToArray());
        }

        internal bool BindFiles(object source, object category, int revision, AssetCheckFileData[] files)
        {
            _report = null;
            if (!ReferenceEquals(_source, source) || !Equals(_category, category))
            { Rule = null; Expanded.Clear(); ShowUnknown = false; }
            return BindCore(source, category, revision, () => files);
        }

        private bool BindCore(object source, object category, int revision, Func<AssetCheckFileData[]> read)
        {
            var contextChanged = !ReferenceEquals(_source, source) || !Equals(_category, category);
            var changed = contextChanged || _revision != revision;
            if (!changed) return false;
            if (!Equals(_category, category)) Rule = null;
            _source = source; _category = category; _revision = revision;
            _entries = read(); Selected.Clear(); Rebuild(); return true;
        }

        internal void Query(string search, string status)
        {
            search = search ?? "";
            if (_search == search && _status == status) return;
            _search = search; _status = status; Selected.Clear(); Rebuild();
        }

        internal void Filter(string rule)
        {
            Rule = rule;
            Selected.Clear();
            Rebuild();
        }

        internal void Rebuild()
        {
            Files = _entries.Select(data => new AssetCheckFileRow(data, Rule))
                .Where(file => (Rule == null || file.VisibleIssues.Length > 0) && Matches(file)).ToList();
            Files.Sort(Compare);
            RebuildRows();
        }

        private bool Matches(AssetCheckFileRow file)
        {
            var data = file.Data;
            var passed = data.Complete && !file.HasProblem && !file.HasUnverified && !data.Candidate;
            var text = data.Path + " " + data.Status + " " + data.Note + " " + string.Join(" ", file.AllIssues.Select(issue => issue.Problem));
            return text.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 &&
                (_status == "全部" || _status == "需要关注" && !passed || _status == "违规" && file.HasProblem || _status == "优化候选" && data.Candidate ||
                 _status == "未能检查" && file.HasUnverified || _status == "未完成" && !data.Complete ||
                 _status == "通过" && passed);
        }

        internal void SortBy(AssetCheckSort column)
        {
            Descending = Sort == column ? !Descending : column == AssetCheckSort.Size;
            Sort = column;
            Rebuild();
        }

        internal void RebuildRows()
        {
            Rows = new List<AssetCheckDisplayRow>();
            if (Sort == AssetCheckSort.Size)
            {
                foreach (var file in Files) AddFile(file);
                return;
            }
            foreach (var file in Files.Where(file => file.HasProblem)) AddFile(file);
            if (UnknownFiles > 0)
            {
                Rows.Add(new AssetCheckDisplayRow { UnknownHeader = true });
                if (ShowUnknown) foreach (var file in Files.Where(file => !file.HasProblem && file.HasUnverified)) AddFile(file);
            }
            foreach (var file in Files.Where(file => !file.HasProblem && !file.HasUnverified)) AddFile(file);
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
            if (Sort == AssetCheckSort.Size)
            {
                var leftRank = !left.Data.SizeBytes.HasValue ? 2 : left.Data.SizePartial ? 1 : 0;
                var rightRank = !right.Data.SizeBytes.HasValue ? 2 : right.Data.SizePartial ? 1 : 0;
                if (leftRank != rightRank) return leftRank.CompareTo(rightRank);
                var sizeOrder = Nullable.Compare(left.Data.SizeBytes, right.Data.SizeBytes);
                if (sizeOrder != 0) return Descending ? -sizeOrder : sizeOrder;
                return StringComparer.Ordinal.Compare(left.Path, right.Path);
            }
            var order = Sort == AssetCheckSort.FileName ? EditorUtility.NaturalCompare(left.Name, right.Name) :
                CompareValue(GroupValue(left), GroupValue(right));
            if (order == 0) order = StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
            return Descending ? -order : order;
        }

        // 保持同一文件的问题分组；多问题文件使用当前排序列的最小值比较。
        private string GroupValue(AssetCheckFileRow file) => file.VisibleIssues.Select(SortValue).DefaultIfEmpty(file.Data.Status ?? "")
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
