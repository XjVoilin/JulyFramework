using System;
using System.Collections.Generic;
using System.Linq;
using July.Release.Editor;

namespace July.AssetCheck
{
    internal sealed class BundleDependencyCause
    {
        internal BundleAnalysisAsset Source, Dependency;
    }

    // 仅由报告建立的不可变包图；不查询当前工程的依赖关系。
    internal sealed class BundlePackageAnalysis
    {
        internal BundleReportAnalysis Analysis { get; }
        internal BundleBuildReport Report => Analysis.Report;
        internal BundleAnalysisAsset[] Assets { get; }
        private readonly Dictionary<string, HashSet<string>> _components = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, BundleAnalysisAsset> _assets;
        private readonly Dictionary<string, string[]> _dependencies = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _bytes = new Dictionary<string, long>(StringComparer.Ordinal);

        internal static string Identity(BundleAnalysisAsset asset) => !string.IsNullOrEmpty(asset.Guid) && asset.Guid.Any(c => c != '0')
            ? "guid:" + asset.Guid.ToLowerInvariant() : "path:" + asset.Path;

        internal BundlePackageAnalysis(BundleBuildReport report)
        {
            Analysis = new BundleReportAnalysis(report);
            Assets = Analysis.Assets.GroupBy(Identity).Select(group =>
            {
                var first = group.OrderBy(asset => asset.Path, StringComparer.Ordinal).First();
                var asset = new BundleAnalysisAsset { Path = first.Path, Guid = first.Guid };
                foreach (var record in group) { asset.Owners.UnionWith(record.Owners); asset.BundleSeeds.UnionWith(record.BundleSeeds); }
                return asset;
            }).OrderBy(asset => asset.Path, StringComparer.Ordinal).ToArray();
            _assets = Assets.ToDictionary(Identity, StringComparer.Ordinal);
            FindCycles();
        }

        internal string[] Dependencies(string name)
        {
            if (!_dependencies.TryGetValue(name, out var dependencies))
                _dependencies.Add(name, dependencies = Analysis.Closure(new[] { name }).Where(x => x != name).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            return dependencies;
        }
        internal long TotalBytes(string name)
        {
            if (!_bytes.TryGetValue(name, out var bytes)) _bytes.Add(name, bytes = Report.Bundles[name].FileSize + Analysis.Bytes(Dependencies(name)));
            return bytes;
        }
        internal AssetBundleResourceSize ResourceSize(BundleAnalysisAsset asset, string owner = null) => Report.Sizes == null ? AssetBundleResourceSize.Unknown :
            owner == null ? Report.Sizes.Resource(asset.Owners, asset.Guid, asset.Path) : Report.Sizes.Resource(owner, asset.Guid, asset.Path);
        internal bool Cyclic(string name) => _components.ContainsKey(name);
        internal string SizeMark(string name, AssetCheckSettings settings) => Report.Bundles[name].FileSize < settings.minBundleBytes ? "过小" :
            Report.Bundles[name].FileSize > settings.maxBundleBytes ? "过大" : "";

        private void FindCycles()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var finish = new List<string>();
            foreach (var name in Report.Bundles.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                var pending = new Stack<(string Name, bool Finish)>(); pending.Push((name, false));
                while (pending.Count > 0)
                {
                    var item = pending.Pop();
                    if (item.Finish) { finish.Add(item.Name); continue; }
                    if (!seen.Add(item.Name)) continue;
                    pending.Push((item.Name, true));
                    foreach (var next in Report.Bundles[item.Name].DependBundles) if (!seen.Contains(next)) pending.Push((next, false));
                }
            }
            seen.Clear();
            for (var i = finish.Count - 1; i >= 0; i--)
            {
                var group = new HashSet<string>(StringComparer.Ordinal);
                var pending = new Stack<string>(); pending.Push(finish[i]);
                while (pending.Count > 0)
                {
                    var name = pending.Pop(); if (!seen.Add(name)) continue;
                    group.Add(name);
                    foreach (var next in Analysis.References(name)) pending.Push(next);
                }
                if (group.Count > 1 || (group.Count == 1 && Report.Bundles[group.First()].DependBundles.Contains(group.First())))
                    foreach (var name in group) _components.Add(name, group);
            }
        }

        internal string[] CyclePath(string start)
        {
            if (!_components.TryGetValue(start, out var group)) return Array.Empty<string>();
            var parents = new Dictionary<string, string>(StringComparer.Ordinal) { [start] = null };
            var pending = new Queue<string>(); pending.Enqueue(start);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                foreach (var next in Report.Bundles[current].DependBundles.Where(group.Contains).OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (next == start)
                    {
                        var path = new List<string>();
                        for (var name = current; name != null; name = parents[name]) path.Add(name);
                        path.Reverse(); path.Add(start); return path.ToArray();
                    }
                    if (parents.ContainsKey(next)) continue;
                    parents.Add(next, current); pending.Enqueue(next);
                }
            }
            throw new InvalidOperationException("循环分量缺少返回起点的路径：" + start);
        }

        internal BundleDependencyCause[] Causes(string from, string to)
        {
            var result = new List<BundleDependencyCause>();
            foreach (var source in Report.Data.AssetInfos.Where(a => a.MainBundleName == from && a.DependBundles.Contains(to)))
            {
                if (source.DependAssets == null) continue;
                var sourceAsset = _assets[Identity(new BundleAnalysisAsset { Path = source.AssetPath, Guid = source.AssetGUID })];
                foreach (var dependency in source.DependAssets)
                {
                    var key = Identity(new BundleAnalysisAsset { Path = dependency.AssetPath, Guid = dependency.AssetGUID });
                    if (!_assets.TryGetValue(key, out var target) || !target.Owners.Contains(to)) continue;
                    if (!result.Any(c => c.Source == sourceAsset && c.Dependency == target))
                        result.Add(new BundleDependencyCause { Source = sourceAsset, Dependency = target });
                }
            }
            return result.ToArray();
        }
    }
}
