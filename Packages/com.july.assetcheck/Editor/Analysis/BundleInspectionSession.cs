using System;
using System.Collections.Generic;
using System.Linq;
using July.Logging;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    internal sealed class BundleResourceCheck
    {
        internal AssetCheckKind Kind;
        internal bool Complete;
        internal readonly List<AssetCheckIssue> Issues = new List<AssetCheckIssue>();
    }

    internal sealed class BundleResourceResult
    {
        internal BundleAnalysisAsset Asset;
        internal string CheckedPath;
        internal readonly List<BundleResourceCheck> Checks = new List<BundleResourceCheck>();
        internal IReadOnlyList<AssetCheckIssue> Issues => Checks.SelectMany(check => check.Issues).ToArray();
        internal readonly List<string> Notes = new List<string>();
        internal string[] SameContent = Array.Empty<string>();
        internal bool Checked;
        internal bool ContentChecked;
        internal bool Candidate => Asset.Owners.Count > 1 || SameContent.Length > 1;
        internal bool Unverified => Notes.Count > 0 || Issues.Any(i => i.Unverified);
        internal bool Problem => Issues.Any(i => i.HasProblem);
        internal string Status
        {
            get
            {
                if (!Checked) return "未完成";
                var status = Problem ? Unverified ? "违规／含未能验证" : "违规" : Unverified ? "未能检查" : Candidate ? "优化候选" : "通过";
                return ContentChecked ? status : status == "通过" ? "未完成" : status + "／内容未完成";
            }
        }
    }

    internal sealed class BundleInspectionSession : IDisposable
    {
        internal BundlePackageAnalysis Packages { get; }
        internal AssetCheckSettings Settings { get; }
        internal BundleResourceResult[] Resources { get; }
        internal DateTime CheckedAt { get; }
        internal bool IsSnapshot { get; }
        internal int Revision { get; private set; }
        internal bool ClassificationReady { get; private set; }
        internal bool Running => _task != null;
        internal bool Cancelled { get; private set; }
        internal bool Complete { get; private set; }
        internal string Failure { get; private set; }
        internal string Activity { get; private set; } = "AB 分析完成，准备检查资源…";
        internal float Progress { get; private set; }
        private IEnumerator<bool> _task;
        private readonly int _revision;
        private readonly Func<bool> _configurationChanged;

        private BundleInspectionSession(BundleBuildReport report, AssetCheckSettings settings)
        {
            Packages = new BundlePackageAnalysis(report);
            Settings = settings.Copy();
            Resources = Packages.Assets.Select(asset => new BundleResourceResult { Asset = asset }).ToArray();
        }

        internal BundleInspectionSession(BundleBuildReport report, AssetCheckSettings settings, DateTime checkedAt) : this(report, settings)
        {
            CheckedAt = checkedAt; IsSnapshot = true; Complete = true; ClassificationReady = true; Progress = 1;
            Activity = "上次检查结果（本地缓存）；资源修改后请到资源检查页复查";
        }

        internal BundleInspectionSession(BundleBuildReport report, AssetCheckSettings settings, Func<bool> configurationChanged) : this(report, settings)
        {
            CheckedAt = DateTime.Now;
            _revision = AssetCheckAssetChanges.Revision;
            _configurationChanged = configurationChanged;
            _task = Inspect().GetEnumerator();
        }

        internal void Step()
        {
            if (!Running) return;
            try
            {
                if (_configurationChanged() || AssetCheckAssetChanges.Revision != _revision)
                {
                    Failure = "检查期间资源或检查配置发生变化，剩余检查已停止；AB 报告结果保持。";
                    Stop(); return;
                }
                if (!_task.MoveNext()) { Complete = true; Progress = 1; Activity = "分析完成"; Stop(); }
            }
            catch (Exception exception)
            {
                Failure = exception.Message; Activity = "资源检查失败，AB 报告结果保留"; Stop(); JLogger.LogException(exception);
            }
            finally { Revision++; }
        }

        internal void Cancel()
        {
            if (!Running) return;
            Revision++; Cancelled = true; Activity = "已取消，保留已完成结果；未完成部分未计为通过"; Stop();
        }
        private void Stop() { _task?.Dispose(); _task = null; }
        public void Dispose() => Cancel();

        private IEnumerable<bool> Inspect()
        {
            // 沿用模块自身的收集契约判定适用性，然后与明确清单相交；不执行目录外文件检查。
            // 同一文件可同时适用于模型、材质等模块，不能按后缀只选一个。
            foreach (var result in Resources) result.CheckedPath = result.Asset.ResolveCurrentPath();
            var roots = Resources.Where(r => !string.IsNullOrEmpty(r.CheckedPath)).Select(r =>
                r.CheckedPath.StartsWith("Packages/", StringComparison.Ordinal) ? string.Join("/", r.CheckedPath.Split('/').Take(2)) : "Assets").Distinct().ToArray();
            var modules = new Dictionary<AssetCheckKind, IAssetCheckModule>();
            foreach (var registration in AssetCheckCatalog.Entries)
            {
                var module = registration.CreateModule();
                var targets = new HashSet<string>(roots.SelectMany(module.Collect), StringComparer.OrdinalIgnoreCase);
                modules.Add(registration.Kind, module);
                foreach (var result in Resources.Where(r => !string.IsNullOrEmpty(r.CheckedPath)))
                    if (targets.Contains(AssetDatabase.AssetPathToGUID(result.CheckedPath, AssetPathToGUIDOptions.OnlyExistingAssets)))
                        result.Checks.Add(new BundleResourceCheck { Kind = registration.Kind });
                Activity = "准备" + registration.Name + "检查"; yield return true;
            }
            ClassificationReady = true;
            for (var index = 0; index < Resources.Length; index++)
            {
                var result = Resources[index];
                var path = result.Asset.ResolveCurrentPath(); result.CheckedPath = path;
                Activity = "检查 " + result.Asset.Path;
                if (string.IsNullOrEmpty(path)) result.Notes.Add("当前工程未匹配报告资源，无法检查");
                else
                {
                    if (result.Checks.Count == 0) result.Notes.Add("当前资源类型没有适用的资源检查模块");
                    foreach (var check in result.Checks)
                    {
                        foreach (var step in AssetCheckExecution.Check(modules[check.Kind], path, Settings.CurrentStandards, check.Issues)) yield return step;
                        check.Complete = true;
                        // 在分类之间让出执行权，取消时不把后续分类计为完成。
                        yield return true;
                    }
                }
                result.Checked = true; Progress = .8f * (index + 1) / Math.Max(1, Resources.Length); yield return true;
            }
            Activity = "检查入包源文件内容重复…";
            var byPath = Resources.Where(r => !string.IsNullOrEmpty(r.CheckedPath)).ToDictionary(r => r.CheckedPath, StringComparer.Ordinal);
            foreach (var step in AssetContentDuplicates.Check(byPath.Keys,
                group => { foreach (var path in group) byPath[path].SameContent = group; },
                (path, message) => byPath[path].Notes.Add(message))) yield return step;
            foreach (var result in Resources) result.ContentChecked = true;
        }
    }
}
