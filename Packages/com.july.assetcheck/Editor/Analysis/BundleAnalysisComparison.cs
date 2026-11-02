using System;
using System.Collections.Generic;
using System.Linq;
using July.Release.Editor;
using UnityEngine;

namespace July.AssetCheck
{
    internal sealed class BundleComparisonRow
    {
        internal string Id, Name;
        internal bool Package, BeforeExists, AfterExists;
        internal long? BeforeBytes, AfterBytes, Delta;
        internal AssetBundleSizeCoverage BeforeCoverage, AfterCoverage;
        internal BundleResourceResult BeforeResource, AfterResource;
        internal readonly List<string> Changes = new List<string>();
        internal bool Changed => Changes.Count > 0;
        internal string Change => string.Join("、", Changes);
    }

    // Build facts and dated inspection evidence only; never queries today's AssetDatabase or applies exclusions.
    internal sealed class BundleAnalysisComparison
    {
        internal BundleInspectionSession Before { get; }
        internal BundleInspectionSession After { get; }
        internal BundleComparisonRow[] Packages { get; }
        internal BundleComparisonRow[] Resources { get; }
        internal bool SameStandards { get; }
        internal string Notice { get; }

        internal BundleAnalysisComparison(BundleInspectionSession before, BundleInspectionSession after)
        {
            if (!before.Complete || !after.Complete) throw new InvalidOperationException("仅可比较两份完整检查结果。");
            Before = before; After = after;
            SameStandards = Standards(before) == Standards(after);
            var a = before.Packages.Report.Data.Summary; var b = after.Packages.Report.Data.Summary;
            var notices = new List<string> { "基线 → 目标；AB 为包文件大小，资源为各物理副本未压缩合计。检查证据来自各自检查时间，不还原构建时配置。" };
            if (a.BuildTarget != b.BuildTarget || a.BuildPackageName != b.BuildPackageName)
                notices.Add("平台或 Package 不同：仅展示差异，不能直接归因为优化收益。");
            if (a.UnityVersion != b.UnityVersion || a.YooVersion != b.YooVersion)
                notices.Add("Unity 或 YooAsset 版本不同：构建方式可能影响体积与依赖，不能直接归因于资源修改。");
            if (!SameStandards) notices.Add("检查标准不同：仅比较问题记录，记录消失不能认定为已修复。");
            if (before.Resources.Any(resource => before.Packages.ResourceSize(resource.Asset).Coverage != AssetBundleSizeCoverage.Complete) ||
                after.Resources.Any(resource => after.Packages.ResourceSize(resource.Asset).Coverage != AssetBundleSizeCoverage.Complete))
                notices.Add("部分资源缺少完整大小；部分/未知统计不计算大小差值。");
            Notice = string.Join("\n", notices);
            Packages = ComparePackages(); Resources = CompareResources();
        }

        static string Standards(BundleInspectionSession session)
        {
            var standards = JsonUtility.FromJson<PlatformStandards>(JsonUtility.ToJson(session.Settings.CurrentStandards));
            standards.texture.allowedFormats = standards.texture.allowedFormats.OrderBy(value => value).ToArray();
            standards.texture.allowedAtlasFormats = standards.texture.allowedAtlasFormats.OrderBy(value => value).ToArray();
            return JsonUtility.ToJson(standards);
        }

        internal static string[] Added(IEnumerable<string> before, IEnumerable<string> after) =>
            after.Except(before, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        static bool Different(IEnumerable<string> a, IEnumerable<string> b) => !new HashSet<string>(a, StringComparer.Ordinal).SetEquals(b);

        BundleComparisonRow[] ComparePackages()
        {
            var before = Before.Packages.Report.Bundles; var after = After.Packages.Report.Bundles;
            var beforeContents = Contents(Before); var afterContents = Contents(After);
            return before.Keys.Union(after.Keys, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).Select(name =>
            {
                bool existsA = before.TryGetValue(name, out var a), existsB = after.TryGetValue(name, out var b);
                var row = new BundleComparisonRow { Id = name, Name = name, Package = true, BeforeExists = existsA, AfterExists = existsB,
                    BeforeBytes = existsA ? a.FileSize : 0, AfterBytes = existsB ? b.FileSize : 0,
                    BeforeCoverage = AssetBundleSizeCoverage.Complete, AfterCoverage = AssetBundleSizeCoverage.Complete };
                row.Delta = row.AfterBytes - row.BeforeBytes;
                if (!existsA) row.Changes.Add("新增包");
                else if (!existsB) row.Changes.Add("删除包");
                else
                {
                    if (a.FileSize != b.FileSize) row.Changes.Add("自身大小变化");
                    if (Different(a.DependBundles, b.DependBundles)) row.Changes.Add("报告依赖变化");
                    if (Different(Before.Packages.Dependencies(name), After.Packages.Dependencies(name))) row.Changes.Add("依赖闭包变化");
                    if (Before.Packages.TotalBytes(name) != After.Packages.TotalBytes(name)) row.Changes.Add("含依赖总计变化");
                    if (Before.Packages.Cyclic(name) != After.Packages.Cyclic(name)) row.Changes.Add("循环状态变化");
                    if (Different(beforeContents[name], afterContents[name])) row.Changes.Add("入包资源变化");
                }
                return row;
            }).ToArray();
        }

        static ILookup<string, string> Contents(BundleInspectionSession session) => session.Resources
            .SelectMany(resource => resource.Asset.Owners.Select(owner => (Owner: owner, Id: BundlePackageAnalysis.Identity(resource.Asset))))
            .ToLookup(item => item.Owner, item => item.Id, StringComparer.Ordinal);

        BundleComparisonRow[] CompareResources()
        {
            var before = Before.Resources.ToDictionary(resource => BundlePackageAnalysis.Identity(resource.Asset), StringComparer.Ordinal);
            var after = After.Resources.ToDictionary(resource => BundlePackageAnalysis.Identity(resource.Asset), StringComparer.Ordinal);
            var beforePaths = CheckedPaths(Before); var afterPaths = CheckedPaths(After);
            return before.Keys.Union(after.Keys, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).Select(id =>
            {
                bool existsA = before.TryGetValue(id, out var a), existsB = after.TryGetValue(id, out var b);
                var sizeA = existsA ? Before.Packages.ResourceSize(a.Asset) : new AssetBundleResourceSize();
                var sizeB = existsB ? After.Packages.ResourceSize(b.Asset) : new AssetBundleResourceSize();
                var row = new BundleComparisonRow { Id = id, Name = (b ?? a).Asset.Path, BeforeExists = existsA, AfterExists = existsB,
                    BeforeResource = a, AfterResource = b, BeforeCoverage = sizeA.Coverage, AfterCoverage = sizeB.Coverage,
                    BeforeBytes = existsA ? sizeA.Coverage == AssetBundleSizeCoverage.Unknown ? (long?)null : sizeA.Bytes : 0,
                    AfterBytes = existsB ? sizeB.Coverage == AssetBundleSizeCoverage.Unknown ? (long?)null : sizeB.Bytes : 0 };
                if ((!existsA || sizeA.Coverage == AssetBundleSizeCoverage.Complete) && (!existsB || sizeB.Coverage == AssetBundleSizeCoverage.Complete))
                    row.Delta = row.AfterBytes - row.BeforeBytes;
                if (!existsA) row.Changes.Add("新增入包");
                else if (!existsB) row.Changes.Add("不再入包");
                else
                {
                    if (a.Asset.Path != b.Asset.Path) row.Changes.Add("路径变化");
                    if (Different(a.Asset.Owners, b.Asset.Owners)) row.Changes.Add("所属包变化");
                    if (sizeA.Coverage != sizeB.Coverage) row.Changes.Add("大小统计完整性变化");
                    else if (row.Delta.HasValue && row.Delta.Value != 0) row.Changes.Add("入包大小变化");
                    else if (sizeA.Coverage == AssetBundleSizeCoverage.Partial && (sizeA.Bytes != sizeB.Bytes || sizeA.Reason != sizeB.Reason))
                        row.Changes.Add("部分大小记录变化");
                    if (Evidence(a) != Evidence(b)) row.Changes.Add("检查记录变化");
                    if (Different(a.SameContent.Select(path => beforePaths[path]), b.SameContent.Select(path => afterPaths[path]))) row.Changes.Add("内容重复关系变化");
                }
                return row;
            }).ToArray();
        }

        static Dictionary<string, string> CheckedPaths(BundleInspectionSession session) => session.Resources
            .Where(result => !string.IsNullOrEmpty(result.CheckedPath)).ToDictionary(result => result.CheckedPath,
                result => BundlePackageAnalysis.Identity(result.Asset), StringComparer.Ordinal);

        sealed class Observation { internal string Key, Label, Value, Description; }
        static Observation[] Observations(BundleResourceResult resource) => resource.Checks.SelectMany(check => check.Issues.SelectMany(issue =>
            (issue.Targets.Count == 0 ? new[] { "" } : issue.Targets).Select(target =>
            {
                var row = AssetCheckIssueSnapshot.Capture(issue);
                row.assetPath = null; row.targetId = null; row.resourceName = null;
                row.targets = target.Length == 0 ? Array.Empty<string>() : new[] { target };
                row.unverified = issue.UnverifiedTargets.Count > 0 ? issue.UnverifiedTargets.Contains(target) : issue.Unverified;
                row.unverifiedTargets = row.unverified ? row.targets : Array.Empty<string>();
                var label = $"{AssetCheckWorkspace.KindName(check.Kind)} / {issue.RuleId} #{issue.LocalId} {issue.Location} {issue.PropertyPath}" + (target.Length == 0 ? "" : " [" + target + "]");
                return new Observation { Key = string.Join("\u001f", check.Kind.ToString(), issue.RuleId, issue.LocalId.ToString(System.Globalization.CultureInfo.InvariantCulture), issue.Location ?? "", issue.PropertyPath ?? "", target),
                    Label = label, Value = JsonUtility.ToJson(row),
                    Description = issue.Problem + "；实际：" + issue.Actual + "；要求：" + issue.Expected + (row.unverified ? "；含未验证" : "") };
            }))).ToArray();

        static string Evidence(BundleResourceResult resource) => string.Join("\n", resource.Checks.Select(check => check.Kind.ToString()).OrderBy(kind => kind, StringComparer.Ordinal)) + "\n" +
            string.Join("\n", Observations(resource).Select(observation => observation.Key + "=" + observation.Value).OrderBy(value => value, StringComparer.Ordinal)) + "\n" +
            string.Join("\n", resource.Notes.OrderBy(note => note, StringComparer.Ordinal));

        static IEnumerable<string> CheckChanges(BundleResourceResult before, BundleResourceResult after)
        {
            var a = Observations(before).GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.OrderBy(item => item.Value, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var b = Observations(after).GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.OrderBy(item => item.Value, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            foreach (var key in a.Keys.Union(b.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
            {
                bool existsA = a.TryGetValue(key, out var previous), existsB = b.TryGetValue(key, out var current);
                if (!existsA) yield return "新增记录：" + current[0].Label + "\n" + string.Join("\n", current.Select(item => item.Description));
                else if (!existsB) yield return "不再出现的记录：" + previous[0].Label + "\n" + string.Join("\n", previous.Select(item => item.Description));
                else if (!previous.Select(item => item.Value).SequenceEqual(current.Select(item => item.Value), StringComparer.Ordinal))
                    yield return "记录内容变化：" + current[0].Label + "\n基线：" + string.Join("\n", previous.Select(item => item.Description)) + "\n目标：" + string.Join("\n", current.Select(item => item.Description));
            }
        }

        internal string[] Details(BundleComparisonRow row)
        {
            var lines = new List<string> { row.Name, row.Changed ? row.Change : "没有变化" };
            if (row.Package)
            {
                var a = row.BeforeExists ? Before.Packages.Report.Bundles[row.Id].DependBundles : Array.Empty<string>();
                var b = row.AfterExists ? After.Packages.Report.Bundles[row.Id].DependBundles : Array.Empty<string>();
                lines.Add("新增报告依赖：" + string.Join("、", Added(a, b)));
                lines.Add("移除报告依赖：" + string.Join("、", Added(b, a)));
                if (row.BeforeExists) lines.Add("基线依赖闭包：" + string.Join("、", Before.Packages.Dependencies(row.Id)) + "\n含依赖总计：" + BundleAnalysisFormatting.Size(Before.Packages.TotalBytes(row.Id)));
                if (row.AfterExists) lines.Add("目标依赖闭包：" + string.Join("、", After.Packages.Dependencies(row.Id)) + "\n含依赖总计：" + BundleAnalysisFormatting.Size(After.Packages.TotalBytes(row.Id)));
                if (row.BeforeExists && Before.Packages.Cyclic(row.Id)) lines.Add("基线循环路径：" + string.Join(" → ", Before.Packages.CyclePath(row.Id)));
                if (row.AfterExists && After.Packages.Cyclic(row.Id)) lines.Add("目标循环路径：" + string.Join(" → ", After.Packages.CyclePath(row.Id)));
                var oldContents = Before.Resources.Where(resource => resource.Asset.Owners.Contains(row.Id)).ToDictionary(resource => BundlePackageAnalysis.Identity(resource.Asset), resource => resource.Asset.Path, StringComparer.Ordinal);
                var newContents = After.Resources.Where(resource => resource.Asset.Owners.Contains(row.Id)).ToDictionary(resource => BundlePackageAnalysis.Identity(resource.Asset), resource => resource.Asset.Path, StringComparer.Ordinal);
                lines.Add("新增入包资源：" + string.Join("、", Added(oldContents.Keys, newContents.Keys).Select(id => newContents[id])));
                lines.Add("移除入包资源：" + string.Join("、", Added(newContents.Keys, oldContents.Keys).Select(id => oldContents[id])));
            }
            else
            {
                if (row.BeforeResource != null && row.AfterResource != null) lines.AddRange(CheckChanges(row.BeforeResource, row.AfterResource));
                ResourceDetails(lines, "基线", row.BeforeResource, Before.CheckedAt);
                ResourceDetails(lines, "目标", row.AfterResource, After.CheckedAt);
            }
            return lines.ToArray();
        }
        static void ResourceDetails(List<string> lines, string title, BundleResourceResult resource, DateTime time)
        {
            if (resource == null) { lines.Add(title + "：未入包"); return; }
            lines.Add(title + "：" + resource.Asset.Path + "\n所属包：" + string.Join("、", resource.Asset.Owners));
            lines.Add($"{title}检查于 {time:yyyy/MM/dd HH:mm:ss}：{resource.Status}");
            foreach (var check in resource.Checks)
                foreach (var issue in check.Issues)
                    lines.Add($"{AssetCheckWorkspace.KindName(check.Kind)} / {issue.RuleId} / {issue.ResourceName}\n{issue.Location} {issue.PropertyPath} #{issue.LocalId}\n{issue.Problem}；实际：{issue.Actual}；要求：{issue.Expected}；目标：{string.Join("、", issue.Targets)}；{(issue.Unverified ? "含未验证" : "已验证记录")}");
            lines.AddRange(resource.Notes);
        }
    }
}
