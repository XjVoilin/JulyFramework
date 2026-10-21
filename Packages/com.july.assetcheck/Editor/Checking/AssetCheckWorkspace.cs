using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace July.AssetCheck
{
    public sealed class AssetCheckWorkspace : IDisposable
    {
        private readonly AssetCheckSettingsFile _file;
        private readonly AssetCheckReportStore _reportStore;
        private readonly Dictionary<AssetCheckKind, string> _saveFailures = new Dictionary<AssetCheckKind, string>();
        private readonly AssetCheckRegistration[] _modules = AssetCheckCatalog.Entries;
        private readonly Dictionary<AssetCheckKind, IAssetCheckModule> _instances;
        private readonly Dictionary<AssetCheckKind, AssetCheckReport> _reports = new Dictionary<AssetCheckKind, AssetCheckReport>();
        private IEnumerator<bool> _task;
        private AssetCheckReport _activeReport;
        private bool _mutating;
        private bool _fixInProgress;
        private string _projectStamp;
        private bool _configurationChanged;

        public AssetCheckSettings Settings { get; private set; }
        public bool Running => _task != null;
        public float Progress { get; private set; }
        public string Activity { get; private set; } = "选择范围后开始检查。";
        public string Failure { get; private set; }
        public AssetCheckFixSummary LastFix { get; private set; }
        public string ReportLoadFailure { get; private set; }
        public string ReportSaveFailure => string.Join("；", _saveFailures.Values);
        public event Action Changed;

        public AssetCheckWorkspace(AssetCheckSettingsFile file)
        {
            _file = file;
            Settings = file.LoadOrCreate();
            _instances = _modules.ToDictionary(entry => entry.Kind, entry => entry.CreateModule());
            _reportStore = new AssetCheckReportStore(file.ProjectDirectory);
            _projectStamp = ProjectStamp();
            LoadReports();
            AssetCheckAssetChanges.Changed += AssetsChanged;
            EditorSceneManager.sceneDirtied += SceneDirtied;
        }

        public AssetCheckReport GetReport(AssetCheckKind kind) => _reports.TryGetValue(kind, out var report) ? report : null;

        public void ChangeSettings(Action<AssetCheckSettings> change)
        {
            RequireIdle();
            var next = Settings.Copy();
            change(next);
            next.Validate();
            if (JsonUtility.ToJson(next) == JsonUtility.ToJson(Settings))
                return;
            var previous = Settings;
            _file.Save(next);
            Settings = next;
            if (AssetCheckExclusions.Key(previous) != AssetCheckExclusions.Key(next))
                UpdateExcludedReports(previous, true);
            foreach (var report in _reports.Values)
                if (report.Context != GetContext(report.Kind)) MarkStale(report);
            Changed?.Invoke();
        }

        public void Ignore(AssetCheckIssue issue)
        {
            RequireIdle();
            var path = AssetCheckTarget.RequirePath(issue.TargetId);
            if (new AssetCheckExclusions(Settings).Contains(issue.TargetId, path))
                return;
            var next = Settings.Copy();
            next.ignoredAssets.Add(new IgnoredAsset { guid = AssetCheckTarget.IsProjectFile(issue.TargetId) ? null : issue.TargetId,
                projectPath = AssetCheckTarget.IsProjectFile(issue.TargetId) ? issue.TargetId : null, lastKnownPath = path, kind = issue.Kind });
            SaveExclusions(next, false);
            Activity = "已忽略文件，已从结果中移除；过期快照请重新检查。";
            Changed?.Invoke();
        }

        public void Restore(string guid)
        {
            RequireIdle();
            var next = Settings.Copy();
            if (next.ignoredAssets.RemoveAll(asset => string.Equals(asset.Id, guid, StringComparison.OrdinalIgnoreCase)) == 0) return;
            SaveExclusions(next, true);
            var path = AssetCheckTarget.Resolve(guid);
            var parent = new AssetCheckExclusions(Settings).FindParentFolder(path);
            Activity = parent != null ? "已移除独立忽略记录，仍由上级文件夹忽略：" + parent :
                string.IsNullOrEmpty(path) ? "已移除失效的忽略记录。" : "已恢复资源，请重新检查。";
            Changed?.Invoke();
        }

        internal void SetFoldersIgnored(string[] paths, bool ignored)
        {
            RequireIdle();
            // Project 窗口的选择属于输入边界，保存前先校验整批目录。
            foreach (var path in paths)
            {
                AssetCheckSettings.ValidateScope(path);
                if (path == "Assets" || !AssetDatabase.IsValidFolder(path))
                    throw new ArgumentException("请选择 Assets 下的具体文件夹：" + path);
            }
            var exclusions = new AssetCheckExclusions(Settings);
            var next = Settings.Copy();
            var changed = false;
            var addedFolders = new List<string>();
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path.Length)
                .ThenBy(path => path, StringComparer.Ordinal))
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (ignored)
                {
                    if (exclusions.Contains(guid, path) || addedFolders.Any(parent =>
                        path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase))) continue;
                    next.ignoredAssets.Add(new IgnoredAsset { guid = guid, lastKnownPath = path });
                    addedFolders.Add(path);
                    changed = true;
                }
                else
                    changed |= next.ignoredAssets.RemoveAll(asset => string.Equals(asset.Id, guid, StringComparison.OrdinalIgnoreCase)) > 0;
            }
            if (!changed) return;
            SaveExclusions(next, true);
            Activity = "文件夹忽略设置已更新，请重新检查。";
            Changed?.Invoke();
        }

        private void SaveExclusions(AssetCheckSettings next, bool requireRescan)
        {
            var previous = Settings;
            _file.Save(next);
            Settings = next;
            UpdateExcludedReports(previous, requireRescan);
        }

        private void UpdateExcludedReports(AssetCheckSettings previous, bool requireRescan)
        {
            var exclusions = new AssetCheckExclusions(Settings);
            foreach (var report in _reports.Values)
            {
                ExcludeKnownTargets(report, exclusions);

                // 更新已过期报告时，保留其原始扫描范围和检查标准。
                var registration = _modules.Single(entry => entry.Kind == report.Kind);
                if (registration.Context(previous) == registration.Context(Settings) && report.Context == GetContext(report.Kind, previous))
                    report.Context = GetContext(report.Kind);
                // 快照未保存通过文件的完整标识，统计总数需要重新扫描确认。
                if ((requireRescan || report.IsSnapshot) && !report.Stale) MarkStale(report);
                else SaveReport(report);
            }
        }

        private static bool ExcludeKnownTargets(AssetCheckReport report, AssetCheckExclusions exclusions)
        {
            var changed = false;
            var ids = report.CheckedTargets.Concat(report.Issues.Select(issue => issue.TargetId)).Distinct().ToArray();
            foreach (var id in ids)
            {
                if (!exclusions.Contains(id, AssetCheckTarget.Resolve(id))) continue;
                report.Exclude(id);
                changed = true;
            }
            return changed;
        }

        public void StartScan(AssetCheckKind? kind = null)
        {
            RequireIdle();
            ValidateScanScope(kind);
            Failure = null;
            LastFix = null;
            Progress = 0;
            _task = Scan(kind).GetEnumerator();
            Changed?.Invoke();
        }

        public void StartFix(AssetCheckKind kind, IEnumerable<AssetCheckIssue> selection)
        {
            RequireIdle();
            PollExternalChanges();
            var report = GetReport(kind);
            if (report == null || report.IsSnapshot || !report.Complete || report.Stale || report.Failure != null)
                throw new InvalidOperationException("请先完成当前标准下的检查。");
            ValidateScanScope(kind);
            var exclusions = new AssetCheckExclusions(Settings);
            var selected = selection.Where(issue => issue.CanFix && issue.Kind == kind)
                .Where(issue => !exclusions.Contains(issue.TargetId, AssetCheckTarget.Resolve(issue.TargetId))).ToArray();
            if (selected.Length == 0)
                return;
            Failure = null;
            Progress = 0;
            _mutating = true;
            _fixInProgress = true;
            LastFix = new AssetCheckFixSummary();
            _activeReport = report;
            report.Stale = true;
            _task = FixAndCheck(kind, selected).GetEnumerator();
            SaveReport(report);
            Changed?.Invoke();
        }

        // EditorWindow 提供编辑器更新驱动；批处理和测试可直接推进工作区。
        public void Step()
        {
            if (_task == null)
                return;
            try
            {
                if (!_task.MoveNext())
                    FinishTask();
            }
            catch (Exception exception)
            {
                // 在操作边界暴露错误并停止执行，不掩盖检查规则失败。
                Failure = exception.Message;
                if (_activeReport != null)
                {
                    _activeReport.Failure = exception.Message;
                    SaveReport(_activeReport);
                }
                if (_fixInProgress)
                    Activity = $"操作失败；已修改 {LastFix.ModifiedFiles} 个文件，跳过 {LastFix.SkippedFiles} 个，请重新检查。";
                Debug.LogException(exception);
                FinishTask();
            }
            Changed?.Invoke();
        }

        public void Cancel()
        {
            if (_task == null)
                return;
            if (_activeReport != null)
                _activeReport.Cancelled = true;
            if (_fixInProgress)
            {
                LastFix.Cancelled = true;
                Activity = $"操作已取消；已修改 {LastFix.ModifiedFiles} 个文件，跳过 {LastFix.SkippedFiles} 个；已保存的修改保留，请重新检查。";
            }
            else
                Activity = "检查已取消，保留已检查的结果。";
            if (_activeReport != null) SaveReport(_activeReport);
            FinishTask();
            Changed?.Invoke();
        }

        public void Dispose()
        {
            Cancel();
            AssetCheckAssetChanges.Changed -= AssetsChanged;
            EditorSceneManager.sceneDirtied -= SceneDirtied;
        }

        private IEnumerable<bool> Scan(AssetCheckKind? kind)
        {
            var modules = _modules.Where(module => !kind.HasValue || module.Kind == kind.Value).ToArray();
            var ignored = new AssetCheckExclusions(Settings);
            for (var moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
            {
                var registration = modules[moduleIndex];
                var module = _instances[registration.Kind];
                var report = new AssetCheckReport(module.Kind, GetContext(module.Kind), Settings.platform,
                    registration.ProjectWide ? "Project" : Settings.scope);
                _activeReport = report;
                _reports[module.Kind] = report;
                SaveReport(report);
                Activity = "正在收集" + KindName(module.Kind) + "资源…";
                yield return true;
                var guids = module.Collect(Settings.scope).OrderBy(guid => AssetCheckTarget.Resolve(guid), StringComparer.Ordinal).ToArray();
                report.CollectedPaths.UnionWith(guids.Select(AssetCheckTarget.Resolve));
                report.CollectionReady = true;
                report.TotalFiles = guids.Length;
                for (var index = 0; index < guids.Length; index++)
                {
                    var guid = guids[index];
                    var path = AssetCheckTarget.Resolve(guid);
                    Activity = "检查 " + path;
                    Progress = (moduleIndex + (float)index / Math.Max(1, guids.Length)) / modules.Length;
                    if (ignored.Contains(guid, path))
                        report.IgnoredTargets.Add(guid);
                    else
                    {
                        AssetCheckTarget.RequirePath(guid);
                        report.InputPaths.Add(path);
                        var issues = new List<AssetCheckIssue>();
                        if (module is IIncrementalAssetCheckModule incremental)
                        {
                            foreach (var step in incremental.CheckIncrementally(path, Settings.CurrentStandards, issues))
                                yield return step;
                        }
                        else
                            issues.AddRange(module.Check(path, Settings.CurrentStandards));
                        report.Issues.AddRange(issues);
                        report.CheckedTargets.Add(guid);
                        if (!AssetCheckTarget.IsProjectFile(path))
                            report.InputPaths.UnionWith(AssetDatabase.GetDependencies(path, true));
                    }
                    report.Revision++;
                    Progress = (moduleIndex + (float)(index + 1) / Math.Max(1, guids.Length)) / modules.Length;
                    yield return true;
                }
                report.Complete = true;
                SaveReport(report);
            }
            Progress = 1;
            Activity = "检查完成。";
        }

        private IEnumerable<bool> FixAndCheck(AssetCheckKind kind, AssetCheckIssue[] selection)
        {
            var module = _instances[kind];
            var groups = selection.GroupBy(issue => issue.TargetId).ToArray();
            var batchImports = kind == AssetCheckKind.Texture;
            var batchSize = batchImports ? 20 : 1;
            for (var start = 0; start < groups.Length; start += batchSize)
            {
                var end = Math.Min(start + batchSize, groups.Length);
                if (batchImports) AssetDatabase.StartAssetEditing();
                try
                {
                    for (var index = start; index < end; index++)
                    {
                        _file.EnsureUnchanged();
                        var group = groups[index];
                        var path = AssetCheckTarget.RequirePath(group.Key);
                        Activity = "修改 " + path;
                        Progress = (float)index / groups.Length;
                        var requested = group.ToDictionary(issue => issue.Key);
                        var current = module.Check(path, Settings.CurrentStandards)
                            .Where(issue => issue.CanFix && requested.ContainsKey(issue.Key))
                            .Select(issue => RestrictFix(issue, requested[issue.Key])).Where(issue => issue != null).ToArray();
                        if (current.Length > 0)
                        {
                            module.Fix(path, current, Settings.CurrentStandards);
                            LastFix.ModifiedFiles++;
                        }
                        else
                            LastFix.SkippedFiles++;
                    }
                }
                finally
                {
                    if (batchImports) AssetDatabase.StopAssetEditing();
                }
                Progress = (float)end / groups.Length;
                yield return true;
            }
            _mutating = false;
            foreach (var step in Scan(kind))
                yield return step;
            var result = GetReport(kind);
            LastFix.Rechecked = result.Complete && !result.Stale;
            Activity = $"已修改 {LastFix.ModifiedFiles} 个文件，跳过 {LastFix.SkippedFiles} 个；" +
                (LastFix.Rechecked ? $"复查剩余 {result.ProblemAssets} 个问题文件，{result.UnverifiedAssets} 个文件含未验证项。" : "复查期间输入发生变化，请重新检查。");
        }

        private string GetContext(AssetCheckKind kind) => GetContext(kind, Settings);
        private string GetContext(AssetCheckKind kind, AssetCheckSettings settings) =>
            _modules.Single(value => value.Kind == kind).Context(settings) + "|规则:" + _instances[kind].RulesFingerprint + "|忽略:" + AssetCheckExclusions.Key(settings);

        private void ValidateScanScope(AssetCheckKind? kind)
        {
            _file.EnsureUnchanged();
            if (_modules.Any(value => (!kind.HasValue || value.Kind == kind.Value) && !value.ProjectWide) &&
                !AssetDatabase.IsValidFolder(Settings.scope))
                throw new DirectoryNotFoundException("扫描文件夹不存在，请重新选择范围：" + Settings.scope);
        }

        private void RequireIdle()
        {
            if (Running)
                throw new InvalidOperationException("请等待当前操作完成，或先取消。");
        }

        private void FinishTask()
        {
            _task.Dispose();
            _task = null;
            _activeReport = null;
            _mutating = false;
            _fixInProgress = false;
        }

        public void PollExternalChanges()
        {
            var configurationChanged = _file.HasExternalChanges;
            if (configurationChanged != _configurationChanged)
            {
                _configurationChanged = configurationChanged;
                if (configurationChanged)
                {
                    foreach (var report in _reports.Values) MarkStale(report);
                    Activity = "检查配置已被外部修改，请重新加载配置。";
                }
                Changed?.Invoke();
            }
            var stamp = ProjectStamp();
            if (stamp == _projectStamp)
                return;
            _projectStamp = stamp;
            foreach (var report in _reports.Values.Where(report => _modules.Single(m => m.Kind == report.Kind).ProjectWide))
                MarkStale(report);
            Changed?.Invoke();
        }

        private static string ProjectStamp() => string.Join("|", AssetCheckTarget.ProjectFiles.Select(path => File.GetLastWriteTimeUtc(path).Ticks));

        private void AssetsChanged(string[] paths)
        {
            foreach (var report in _reports.Values)
            {
                var registration = _modules.Single(entry => entry.Kind == report.Kind);
                // 源图可能由扫描目录外的图集收录；新增、移除或编辑图集会改变格式规则。
                if (report.Kind == AssetCheckKind.Texture && Settings.CurrentStandards.texture.requireUncompressedAtlasSources &&
                    paths.Any(path => path.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase) ||
                                      path.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase)))
                {
                    MarkStale(report);
                    continue;
                }
                foreach (var path in paths)
                {
                    var inScope = !registration.ProjectWide && (path == Settings.scope || path.StartsWith(Settings.scope + "/", StringComparison.Ordinal));
                    var affected = report.InputPaths.Contains(path) || report.CollectedPaths.Contains(path) || inScope;
                    if (ReferenceEquals(report, _activeReport) && !_mutating)
                        affected = report.InputPaths.Contains(path) ||
                            (report.CollectionReady && (registration.ProjectWide ? report.CollectedPaths.Contains(path) :
                                inScope && !report.CollectedPaths.Contains(path)));
                    if (affected)
                    {
                        MarkStale(report);
                        break;
                    }
                }
            }
            Changed?.Invoke();
        }

        private void SceneDirtied(Scene scene)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var path = stage != null && stage.scene == scene ? stage.assetPath : scene.path;
            foreach (var report in _reports.Values)
                if (report.InputPaths.Contains(path))
                    MarkStale(report);
            Changed?.Invoke();
        }

        private static AssetCheckIssue RestrictFix(AssetCheckIssue current, AssetCheckIssue requested)
        {
            if (requested.Targets.Count == 0) return current;
            var available = !requested.Unverified ? current.Targets.Except(current.UnverifiedTargets) : current.Targets;
            var targets = available.Intersect(requested.Targets).ToArray();
            return targets.Length == 0 ? null : current.ForTargets(targets, requested.Unverified, requested.HasProblem);
        }

        private void LoadReports()
        {
            var errors = new List<string>();
            var exclusions = new AssetCheckExclusions(Settings);
            foreach (var registration in _modules)
            {
                try
                {
                    var report = _reportStore.Load(registration.Kind);
                    if (report == null) continue;
                    report.Stale |= report.Context != GetContext(report.Kind);
                    if (ExcludeKnownTargets(report, exclusions)) report.Stale = true;
                    _reports.Add(report.Kind, report);
                }
                catch (InvalidDataException exception) { errors.Add(registration.Name + "上次报告无效：" + exception.Message); }
                catch (IOException exception) { errors.Add(registration.Name + "上次报告无法读取：" + exception.Message); }
                catch (UnauthorizedAccessException exception) { errors.Add(registration.Name + "上次报告无法读取：" + exception.Message); }
            }
            ReportLoadFailure = string.Join("；", errors);
            if (_reports.Count > 0) Activity = "已显示上次结果快照；重新检查后可修改。";
        }

        private void MarkStale(AssetCheckReport report)
        {
            if (report.Stale) return;
            report.Stale = true;
            report.Revision++;
            SaveReport(report);
        }

        private void SaveReport(AssetCheckReport report)
        {
            try
            {
                _reportStore.Save(report, ReferenceEquals(report, _activeReport) && _mutating);
                _saveFailures.Remove(report.Kind);
            }
            catch (IOException exception) { RecordSaveFailure(report.Kind, exception); }
            catch (UnauthorizedAccessException exception) { RecordSaveFailure(report.Kind, exception); }
        }

        private void RecordSaveFailure(AssetCheckKind kind, Exception exception)
        {
            var message = KindName(kind) + "结果保存失败：" + exception.Message;
            // 报告文件属于本地缓存；保存失败时保留内存结果，并明确提示读写错误。
            if (!_saveFailures.TryGetValue(kind, out var previous) || previous != message)
                Debug.LogError(message);
            _saveFailures[kind] = message;
        }

        public static string KindName(AssetCheckKind kind) => AssetCheckCatalog.Entries.Single(value => value.Kind == kind).Name;
    }
}
