using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace July.AssetCheck
{
    internal enum BundleAnalysisMode { BundleSizes, Assets, Duplicates, Dependencies, Granularity, Directories }
    internal enum BundleDependencyMetric { AssetLoad, BundleClosure }

    internal sealed class BundleAnalysisAsset
    {
        internal string Path, Guid;
        internal readonly SortedSet<string> Owners = new SortedSet<string>(StringComparer.Ordinal);
        internal readonly HashSet<string> BundleSeeds = new HashSet<string>(StringComparer.Ordinal);
        // 仅 AssetInfos 中的主资源有独立加载入口；null 表示报告未提供，不能用所属包闭包替代。
        internal string[] LoadBundles;

        internal string ResolveCurrentPath()
        {
            if (!string.IsNullOrEmpty(Guid) && Guid.Any(character => character != '0'))
            {
                var path = AssetDatabase.GUIDToAssetPath(Guid);
                // GUIDToAssetPath 会包含本次编辑器会话中已删除的资源缓存，需核对现存文件身份。
                return !string.IsNullOrEmpty(path) && string.Equals(
                    AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets), Guid, StringComparison.OrdinalIgnoreCase) ? path : "";
            }
            return (Path.StartsWith("Assets/", StringComparison.Ordinal) || Path.StartsWith("Packages/", StringComparison.Ordinal)) &&
                !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(Path, AssetPathToGUIDOptions.OnlyExistingAssets)) ? Path : "";
        }
    }

    internal sealed class BundleAnalysisRow
    {
        internal string Name, Path, Description, Bundle;
        internal long? Bytes;
        internal BundleAnalysisAsset Asset;
        internal BundleAnalysisAsset[] Assets;
        internal string[] Bundles;
        internal BundleDependencyMetric DependencyMetric;
    }

    internal enum BundleDetailKind { Information, Asset, Association, Dependency, Reference }

    internal sealed class BundleAnalysisDetail
    {
        internal string Text, Bundle;
        internal BundleAnalysisAsset Asset;
        internal BundleDetailKind Kind;
    }

    internal sealed class BundleReportAnalysis
    {
        internal BundleBuildReport Report { get; }
        internal IReadOnlyCollection<BundleAnalysisAsset> Assets => _assets.Values;
        private readonly Dictionary<string, BundleAnalysisAsset> _assets = new Dictionary<string, BundleAnalysisAsset>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _closures = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        internal BundleReportAnalysis(BundleBuildReport report)
        {
            Report = report;
            foreach (var bundle in report.Bundles.Values) _references.Add(bundle.BundleName, new HashSet<string>(StringComparer.Ordinal));
            foreach (var bundle in report.Bundles.Values)
            {
                foreach (var dependency in bundle.DependBundles) _references[dependency].Add(bundle.BundleName);
                foreach (var source in bundle.BundleContents)
                {
                    var asset = AddAsset(source);
                    asset.Owners.Add(bundle.BundleName);
                    asset.BundleSeeds.Add(bundle.BundleName);
                }
            }
            foreach (var source in report.Data.AssetInfos)
            {
                var asset = AddAsset(source);
                asset.Owners.Add(source.MainBundleName);
                asset.LoadBundles = new[] { source.MainBundleName }.Concat(source.DependBundles)
                    .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                asset.BundleSeeds.UnionWith(asset.LoadBundles);
            }
        }

        private BundleAnalysisAsset AddAsset(BundleReportAssetData source)
        {
            if (!_assets.TryGetValue(source.AssetPath, out var asset))
            {
                asset = new BundleAnalysisAsset { Path = source.AssetPath, Guid = source.AssetGUID };
                _assets.Add(asset.Path, asset);
            }
            else if (string.IsNullOrEmpty(asset.Guid)) asset.Guid = source.AssetGUID;
            else if (!string.IsNullOrEmpty(source.AssetGUID) && !string.Equals(asset.Guid, source.AssetGUID, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("同一路径在报告中对应不同 GUID：" + source.AssetPath);
            return asset;
        }

        internal HashSet<string> Closure(IEnumerable<string> seeds)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var seed in seeds)
            {
                if (!_closures.TryGetValue(seed, out var closure))
                {
                    closure = new HashSet<string>(StringComparer.Ordinal);
                    var pending = new Stack<string>();
                    pending.Push(seed);
                    while (pending.Count > 0)
                    {
                        var name = pending.Pop();
                        if (!closure.Add(name)) continue; // 包依赖可成环，同一包只统计一次。
                        foreach (var dependency in Report.Bundles[name].DependBundles) pending.Push(dependency);
                    }
                    _closures.Add(seed, closure);
                }
                result.UnionWith(closure);
            }
            return result;
        }

        internal long Bytes(IEnumerable<string> bundles) => bundles.Distinct(StringComparer.Ordinal).Sum(name => Report.Bundles[name].FileSize);
        internal IEnumerable<string> References(string name) => _references[name].OrderBy(value => value, StringComparer.Ordinal);
        internal BundleAnalysisProjection Project(AssetCheckSettings settings) => new BundleAnalysisProjection(this, settings);
    }

    internal sealed class BundleAnalysisProjection
    {
        private readonly BundleReportAnalysis _analysis;
        private readonly HashSet<string> _visible = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _currentPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        internal List<BundleAnalysisAsset> Assets { get; } = new List<BundleAnalysisAsset>();
        internal HashSet<string> RelatedBundles { get; }
        internal int IgnoredCount { get; }
        internal int UnmatchedCount { get; }
        internal int UnresolvedIgnoreCount { get; }
        internal long RelatedBytes => _analysis.Bytes(RelatedBundles);

        internal BundleAnalysisProjection(BundleReportAnalysis analysis, AssetCheckSettings settings)
        {
            _analysis = analysis;
            var exclusions = new AssetCheckExclusions(settings);
            UnresolvedIgnoreCount = exclusions.UnresolvedHistoricalRecords;
            foreach (var asset in analysis.Assets)
            {
                var currentPath = asset.ResolveCurrentPath();
                var path = string.IsNullOrEmpty(currentPath) ? asset.Path : currentPath;
                if (!path.StartsWith(settings.scope + "/", StringComparison.OrdinalIgnoreCase)) continue;
                var id = string.IsNullOrEmpty(asset.Guid) || asset.Guid.All(character => character == '0')
                    ? AssetDatabase.AssetPathToGUID(currentPath, AssetPathToGUIDOptions.OnlyExistingAssets) : asset.Guid;
                var ignored = string.IsNullOrEmpty(currentPath) ? exclusions.ContainsHistorical(id, asset.Path) : exclusions.Contains(id, path);
                if (ignored) { IgnoredCount++; continue; }
                if (string.IsNullOrEmpty(currentPath)) UnmatchedCount++;
                Assets.Add(asset);
                _visible.Add(asset.Path);
                _currentPaths.Add(asset.Path, currentPath);
            }
            RelatedBundles = analysis.Closure(Assets.SelectMany(asset => asset.BundleSeeds));
            // 无资源清单的生成包没有可筛选路径，全 Assets 视图保留其报告记录。
            if (settings.scope == "Assets")
                RelatedBundles.UnionWith(analysis.Report.Bundles.Values.Where(bundle => bundle.BundleContents.Length == 0).Select(bundle => bundle.BundleName));
        }

        internal string DisplayPath(BundleAnalysisAsset asset) => string.IsNullOrEmpty(_currentPaths[asset.Path]) ? asset.Path : _currentPaths[asset.Path];
        internal string MatchLabel(BundleAnalysisAsset asset) => string.IsNullOrEmpty(_currentPaths[asset.Path]) ? "当前工程未匹配" :
            string.IsNullOrEmpty(asset.Guid) || asset.Guid.All(character => character == '0') ? "按路径匹配" : "GUID 匹配";

        internal List<BundleAnalysisRow> Rows(BundleAnalysisMode mode, BundleDependencyMetric metric = BundleDependencyMetric.AssetLoad)
        {
            if (mode == BundleAnalysisMode.BundleSizes || mode == BundleAnalysisMode.Granularity)
            {
                var rows = RelatedBundles.Select(BundleRow);
                return (mode == BundleAnalysisMode.Granularity ? rows.OrderBy(row => row.Bytes) : rows.OrderByDescending(row => row.Bytes))
                    .ThenBy(row => row.Name, StringComparer.Ordinal).ToList();
            }
            if (mode == BundleAnalysisMode.Directories)
                return Assets.GroupBy(asset => System.IO.Path.GetDirectoryName(DisplayPath(asset))?.Replace('\\', '/') ?? "", StringComparer.Ordinal)
                    .Select(group =>
                    {
                        var bundles = _analysis.Closure(group.SelectMany(asset => asset.BundleSeeds)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                        return new BundleAnalysisRow
                        {
                            Name = group.Key, Path = group.Key, Bytes = _analysis.Bytes(bundles), Assets = group.ToArray(), Bundles = bundles,
                            Description = $"{group.Count()} 个资源，包级关联闭包 {bundles.Length} 个包（目录间可能重叠）"
                        };
                    }).OrderByDescending(row => row.Bytes).ThenBy(row => row.Path, StringComparer.Ordinal).ToList();

            var assets = mode == BundleAnalysisMode.Duplicates ? Assets.Where(asset => asset.Owners.Count > 1) : Assets;
            var result = assets.Select(asset => AssetRow(asset, mode, metric));
            return mode == BundleAnalysisMode.Dependencies ? result.OrderByDescending(row => row.Bytes).ThenBy(row => row.Path, StringComparer.Ordinal).ToList() :
                result.OrderByDescending(row => mode == BundleAnalysisMode.Duplicates ? row.Asset.Owners.Count : 0).ThenBy(row => row.Path, StringComparer.Ordinal).ToList();
        }

        internal BundleAnalysisRow AssetRow(BundleAnalysisAsset asset, BundleAnalysisMode mode = BundleAnalysisMode.Assets,
            BundleDependencyMetric metric = BundleDependencyMetric.AssetLoad)
        {
            IEnumerable<string> bundleSet = asset.Owners;
            long? bytes = null;
            var description = $"{asset.Owners.Count} 个所属包";
            if (mode == BundleAnalysisMode.Dependencies)
            {
                if (metric == BundleDependencyMetric.AssetLoad)
                {
                    bundleSet = asset.LoadBundles ?? Array.Empty<string>();
                    if (asset.LoadBundles != null) bytes = _analysis.Bytes(bundleSet);
                    description = asset.LoadBundles == null ? "报告未提供独立加载集合（仅包内资源）" :
                        $"资源加载包集合 {asset.LoadBundles.Length} 个包（主包＋报告资源依赖）";
                }
                else
                {
                    bundleSet = _analysis.Closure(asset.BundleSeeds);
                    bytes = _analysis.Bytes(bundleSet);
                    description = $"包级关联闭包 {bundleSet.Count()} 个包";
                }
            }
            return new BundleAnalysisRow
            {
                Name = System.IO.Path.GetFileName(DisplayPath(asset)), Path = DisplayPath(asset), Asset = asset, Assets = new[] { asset },
                Bundles = bundleSet.OrderBy(name => name, StringComparer.Ordinal).ToArray(), DependencyMetric = metric,
                Bytes = bytes, Description = description + $" · {MatchLabel(asset)}"
            };
        }

        internal BundleAnalysisRow BundleRow(string name)
        {
            var bundle = _analysis.Report.Bundles[name];
            var assets = Assets.Where(asset => asset.Owners.Contains(name)).ToArray();
            return new BundleAnalysisRow
            {
                Name = name, Path = bundle.FileName, Bundle = name, Bytes = bundle.FileSize,
                Assets = assets, Bundles = new[] { name },
                Description = $"报告内资源 {bundle.BundleContents.Length}，当前可见 {assets.Length}，依赖 {bundle.DependBundles.Length}，被引用 {_analysis.References(name).Count()}" +
                    (assets.Length == 0 ? " · 依赖／生成包上下文" : "")
            };
        }

        internal List<BundleAnalysisDetail> Details(BundleAnalysisRow row, BundleAnalysisMode mode)
        {
            var details = new List<BundleAnalysisDetail>();
            if (row.Bundle != null)
            {
                var bundle = _analysis.Report.Bundles[row.Bundle];
                details.Add(new BundleAnalysisDetail { Text = "资源包：" + bundle.BundleName + "\n文件：" + bundle.FileName + "；标签：" + string.Join("、", bundle.Tags) });
                foreach (var asset in row.Assets.OrderBy(asset => DisplayPath(asset), StringComparer.Ordinal)) details.Add(AssetDetail(asset));
                foreach (var dependency in bundle.DependBundles) details.Add(BundleDetail(dependency, "依赖包", BundleDetailKind.Dependency));
                foreach (var reference in _analysis.References(row.Bundle)) details.Add(BundleDetail(reference, "引用此包", BundleDetailKind.Reference));
            }
            else
            {
                var loadSet = mode == BundleAnalysisMode.Dependencies && row.DependencyMetric == BundleDependencyMetric.AssetLoad;
                if (mode == BundleAnalysisMode.Dependencies)
                    details.Add(new BundleAnalysisDetail { Text = loadSet ? row.Asset.LoadBundles == null ?
                        "报告未提供独立加载集合；包内资源不能据所属包推算独立加载成本。" :
                        "资源加载包集合：主包＋报告资源依赖，去重后合计；不扩展同包其他资源的依赖。" :
                        "包级关联闭包：全部所属包及报告资源依赖沿包级依赖展开，可能包含同包其他资源的依赖。" });
                foreach (var asset in row.Assets) details.Add(AssetDetail(asset));
                foreach (var name in row.Bundles) details.Add(BundleDetail(name, row.Asset == null ? "目录关联包" : loadSet ? "资源加载包" : "资源关联包"));
                if ((mode == BundleAnalysisMode.Dependencies && !loadSet) || mode == BundleAnalysisMode.Directories)
                    foreach (var name in row.Bundles)
                        foreach (var dependency in _analysis.Report.Bundles[name].DependBundles)
                            details.Add(new BundleAnalysisDetail { Bundle = dependency, Kind = BundleDetailKind.Dependency, Text = "依赖关系：" + name + " → " + dependency });
            }
            return details;
        }

        private BundleAnalysisDetail AssetDetail(BundleAnalysisAsset asset) => new BundleAnalysisDetail
        {
            Asset = asset, Kind = BundleDetailKind.Asset, Text = DisplayPath(asset) + "\n" + MatchLabel(asset) + (DisplayPath(asset) == asset.Path ? "" : "；报告路径：" + asset.Path)
        };

        private BundleAnalysisDetail BundleDetail(string name, string prefix, BundleDetailKind kind = BundleDetailKind.Association) => new BundleAnalysisDetail
        {
            Bundle = name, Kind = kind, Text = prefix + "：" + name + "\n" + BundleAnalysisFormatting.Size(_analysis.Report.Bundles[name].FileSize)
        };

        internal string Distribution()
        {
            var sizes = RelatedBundles.Select(name => _analysis.Report.Bundles[name].FileSize).ToArray();
            return $"关联包分布：≤64 KiB {sizes.Count(size => size <= 64 * 1024)}；64–256 KiB {sizes.Count(size => size > 64 * 1024 && size <= 256 * 1024)}；" +
                $"256 KiB–1 MiB {sizes.Count(size => size > 256 * 1024 && size <= 1024 * 1024)}；>1 MiB {sizes.Count(size => size > 1024 * 1024)}。区间仅用于统计。";
        }
    }

    internal static class BundleAnalysisFormatting
    {
        internal static string Size(long bytes) => bytes >= 1024 * 1024 ? (bytes / (1024d * 1024)).ToString("N2") + " MiB" :
            bytes >= 1024 ? (bytes / 1024d).ToString("N2") + " KiB" : bytes + " B";
    }
}
