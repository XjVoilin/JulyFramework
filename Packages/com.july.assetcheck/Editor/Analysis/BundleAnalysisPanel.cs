using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using July.Logging;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using July.Release.Editor;

namespace July.AssetCheck
{
    internal sealed class BundleAnalysisPanel : VisualElement, IDisposable
    {
        private readonly Func<AssetCheckWorkspace> _workspace;
        private readonly Action _changed;
        private readonly BundleAnalysisCache _cache;
        private string _cacheNotice;
        private readonly VisualElement _singlePage;
        private readonly BundleComparisonView _comparison;
        private readonly PopupField<BundleAnalysisRecord> _history;
        private readonly Button _openHistory, _compareHistory, _current, _recheck;
        private BundleInspectionSession _savedSession;
        private BundleAnalysisRecord _savedRecord;
        private bool _comparing;
        private readonly TextField _path, _search, _directory;
        private readonly VisualElement _directoryTools;
        private readonly Label _source, _summary, _status, _counts, _configuration;
        private readonly Button _read, _build, _cancel, _packsTab, _resourcesTab;
        private readonly Button _chooseReport;
        private readonly PopupField<BuildTarget> _target;
        private readonly Label _activeTarget;
        private readonly Column _resourceSizeColumn;
        private readonly PopupField<string> _filter;
        private readonly MultiColumnListView _packs;
        private readonly AssetCheckResultsView _resources;
        private readonly AssetCheckTypeTabs _types;
        private readonly Button _unclassified;
        private AssetCheckKind? _kind = AssetCheckKind.Texture;
        private double _nextResultsRefresh;
        private readonly ScrollView _inspector;
        private readonly ProgressBar _progress;
        private readonly Foldout _standards;
        private readonly IntegerField _minimum;
        private readonly FloatField _maximum;
        private bool _resourceView, _descending = true;
        private string _sort = "size", _owner;
        private List<string> _packRows = new List<string>();

        private BundleResourceResult _selectedResource;
        private string _selectedPack;
        private bool _rebinding;
        internal BundleInspectionSession Session { get; private set; }
        internal bool Running => BundleAnalysisBuildQueue.Running || (Session != null && Session.Running);

        internal BundleAnalysisPanel(Func<AssetCheckWorkspace> workspace, Action changed)
        {
            _workspace = workspace; _changed = changed; name = "bundle-analysis-page";
            _cache = new BundleAnalysisCache(_workspace().ProjectDirectory);
            AddToClassList("bundle-analysis"); AddToClassList(EditorGUIUtility.isProSkin ? "analysis-dark" : "analysis-light");
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.assetcheck/Editor/Analysis/BundleAnalysisPanel.uss"));
            style.flexGrow = 1; style.minHeight = 0;
            var input = Row();
            _path = new TextField("报告") { name = "bundle-report-path", style = { flexGrow = 1, flexShrink = 1, flexBasis = 0, minWidth = 0 } }; _path.labelElement.style.minWidth = 36; input.Add(_path);
            _chooseReport = new Button(() => { var path = EditorUtility.OpenFilePanel("选择 YooAsset report", "", "report"); if (path.Length > 0) _path.value = path; }) { text = "选择 .report" }; input.Add(_chooseReport);
            _read = new Button(() => RunUi(() => ImportReport(_path.value))) { name = "analyze-report", text = "开始分析" }; input.Add(_read);
            _history = new PopupField<BundleAnalysisRecord>("历史", new List<BundleAnalysisRecord> { null }, 0)
            { name = "analysis-history", formatSelectedValueCallback = record => record?.ToString() ?? "尚无完整结果",
                formatListItemCallback = record => record?.ToString() ?? "尚无完整结果", style = { width = 235, flexShrink = 1 } };
            _history.labelElement.style.minWidth = 30; input.Add(_history);
            _openHistory = new Button(() => RunUi(() => { var id = _history.value.Id; SaveCache(); RestoreCache(id); })) { name = "open-analysis-history", text = "查看" }; input.Add(_openHistory);
            _recheck = new Button(() => RunUi(() => BeginInspection(Session.Packages.Report)))
                { name = "recheck-analysis-history", text = "重检资源", tooltip = "按当前显示的归档报告确定入包范围，检查当前工程；不构建 AB。" }; input.Add(_recheck);
            _compareHistory = new Button(() => RunUi(() => { _comparison.RefreshRecords(); ShowComparison(true); })) { name = "show-analysis-comparison", text = "结果对比" }; input.Add(_compareHistory);
            _current = new Button(() => ShowComparison(false)) { name = "show-analysis-current", text = "返回结果" }; input.Add(_current);
            Add(input);
            _singlePage = new VisualElement { name = "analysis-single", style = { flexGrow = 1, minHeight = 0 } }; Add(_singlePage);
            var buildTools = Row();
            var targets = BundleAnalysisBuildQueue.SupportedTargets().ToList();
            var selected = BundleAnalysisBuildQueue.Current?.target ?? EditorUserBuildSettings.activeBuildTarget;
            _target = new PopupField<BuildTarget>("构建目标", targets, targets.Contains(selected) ? selected : targets.FirstOrDefault())
            { name = "analysis-build-target", tooltip = "Unity 构建目标；只列出当前引擎已安装支持的平台。与发布渠道配置分开。", style = { width = 300 } };
            buildTools.Add(_target);
            _activeTarget = Label("analysis-active-target"); _activeTarget.style.flexGrow = 1; _activeTarget.style.flexShrink = 1; _activeTarget.style.flexBasis = 0; _activeTarget.style.minWidth = 0; buildTools.Add(_activeTarget);
            _build = new Button(() => RunUi(BuildAndAnalyze)) { name = "build-analysis-bundles", text = "构建 AB 并分析", tooltip = "切换平台并完成脚本编译后自动构建；沿用现有 DLL 输入，输出到独立分析目录。不会执行正式发布准备，完成后保留目标平台。" }; buildTools.Add(_build); _singlePage.Add(buildTools);
            _target.RegisterValueChangedCallback(_ => RefreshBuildControls());
            _source = Label("report-source"); _singlePage.Add(_source);
            _configuration = Label("analysis-configuration", "配置已被外部修改或移除，请通过设置重新加载；已完成的分析快照保留。"); _singlePage.Add(_configuration);
            _summary = Label("report-summary"); _singlePage.Add(_summary);
            _standards = new Foldout { text = "AB 大小标准（资源标准沿用资源检查页）", value = false }; var fields = Row();
            _minimum = new IntegerField("最小（KiB）") { style = { width = 260, flexShrink = 1 } }; _maximum = new FloatField("最大（MiB）") { style = { width = 260, flexShrink = 1 } }; fields.Add(_minimum); fields.Add(_maximum); _standards.Add(fields); _singlePage.Add(_standards);
            _minimum.RegisterValueChangedCallback(e => RunUi(() => _workspace().ChangeSettings(s => s.minBundleBytes = checked(e.newValue * 1024))));
            _maximum.RegisterValueChangedCallback(e => RunUi(() => _workspace().ChangeSettings(s => s.maxBundleBytes = checked((int)(e.newValue * 1048576)))));
            var tabs = Row(); _packsTab = new Button(() => ShowResources(false)) { name = "analysis-packages-tab", text = "AB 包结果" };
            _resourcesTab = new Button(() => ShowResources(true)) { name = "analysis-resources-tab", text = "资源检查结果" }; tabs.Add(_packsTab); tabs.Add(_resourcesTab); _singlePage.Add(tabs);
            _types = new AssetCheckTypeTabs(kind => SelectResourceType(kind), "bundle-tab-") { name = "bundle-resource-types" };
            _unclassified = new Button(() => SelectResourceType(null)) { name = "bundle-tab-unclassified", text = "未归类" };
            _unclassified.AddToClassList("type-tab"); _types.Add(_unclassified); _singlePage.Add(_types);
            _directoryTools = Row(); _directoryTools.name = "resource-directory-tools";
            _directoryTools.style.marginTop = 0; _directoryTools.style.marginBottom = 0;
            _directory = new TextField("目录") { name = "resource-directory", isDelayed = true, tooltip = "填写后按回车或移出焦点应用。按表格记录路径筛选，包含子目录；留空为全部。历史目录无需当前存在。", style = { flexGrow = 1, flexShrink = 1, flexBasis = 0, minWidth = 0 } };
            _directory.labelElement.style.minWidth = 36; _directoryTools.Add(_directory);
            _directory.RegisterValueChangedCallback(change => { _directory.SetValueWithoutNotify(change.previousValue); RunUi(() => SetResourceDirectory(change.newValue)); });
            _directoryTools.Add(new Button(() => RunUi(() =>
            {
                var path = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (!AssetDatabase.IsValidFolder(path)) throw new InvalidOperationException("请在 Project 中选择一个文件夹。");
                SetResourceDirectory(path);
            })) { name = "resource-directory-selected", text = "使用选中目录" });
            _directoryTools.Add(new Button(() => SetResourceDirectory("")) { name = "resource-directory-all", text = "全部目录" });
            _singlePage.Add(_directoryTools);
            var tools = Row(); _search = new TextField("搜索") { name = "analysis-search", style = { flexGrow = 1, flexShrink = 1, flexBasis = 0, minWidth = 0 } }; _search.labelElement.style.minWidth = 36; tools.Add(_search);
            _filter = new PopupField<string>(new List<string> { "全部", "过大", "过小", "循环依赖" }, 0) { name = "analysis-filter", style = { width = 150, flexShrink = 0 } }; tools.Add(_filter);
            tools.Add(new Button(() => { _owner = null; _directory.SetValueWithoutNotify(""); _filter.SetValueWithoutNotify("全部"); _search.SetValueWithoutNotify(""); Rebuild(); }) { text = "清除筛选" });
            _search.RegisterValueChangedCallback(_ => Rebuild()); _filter.RegisterValueChangedCallback(_ => Rebuild()); _singlePage.Add(tools);
            _counts = Label("analysis-counts"); _singlePage.Add(_counts);
            _packs = List("analysis-results");
            Column(_packs, "name", "AB 包", 230, i => _packRows[i], i => InspectPackage(_packRows[i]));
            Column(_packs, "size", "自身大小", 90, i => Size(Pack(_packRows[i]).FileSize), sort: "size");
            Column(_packs, "deps", "依赖包数", 75, i => Session.Packages.Dependencies(_packRows[i]).Length.ToString(), sort: "deps");
            Column(_packs, "total", "含依赖总计", 105, i => Size(Session.Packages.TotalBytes(_packRows[i])), sort: "total");
            Column(_packs, "marks", "标记", 110, i => Marks(_packRows[i]));
            _packs.selectionChanged += selected => { if (_rebinding) return; var name = selected.OfType<string>().FirstOrDefault(); if (name != null) InspectPackage(name); };
            _resources = new AssetCheckResultsView(LocateResource) { name = "resource-results" };
            _resourceSizeColumn = _resources.AddSizeColumn(file => ResourceSizeCaption(file.Data.SizeBytes, file.Data.SizePartial));
            _resources.AddRelatedColumn("所属 AB", file => Resource(file.Id).Asset.Owners.Count + " 个包", file => InspectResource(Resource(file.Id)));
            var lists = new VisualElement { style = { flexGrow = 1, minWidth = 490, minHeight = 0 } }; lists.Add(_packs); lists.Add(_resources);
            _inspector = new ScrollView { name = "analysis-inspector", style = { minWidth = 280, flexGrow = 1 } }; _inspector.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _resources.SelectionChanged += () =>
            {
                if (_selectedResource != null && !_resources.Model.Files.Any(file => file.Id == BundlePackageAnalysis.Identity(_selectedResource.Asset)))
                { _selectedResource = null; _inspector.Clear(); }
            };
            var split = new TwoPaneSplitView(1, 305, TwoPaneSplitViewOrientation.Horizontal) { name = "analysis-split", style = { flexGrow = 1, minHeight = 160 } }; split.Add(lists); split.Add(_inspector); _singlePage.Add(split);
            var footer = Row(); _status = Label("analysis-status"); _status.style.flexGrow = 1; _status.style.flexShrink = 1; _status.style.flexBasis = 0; _status.style.minWidth = 0; footer.Add(_status);
            _cancel = new Button(() => RunUi(() => { if (BundleAnalysisBuildQueue.Running) BundleAnalysisBuildQueue.Cancel(); else Session.Cancel(); RefreshProgress(); if (_selectedResource != null) InspectResource(_selectedResource); _changed(); })) { name = "analysis-cancel", text = "取消检查" }; footer.Add(_cancel); _singlePage.Add(footer);
            _progress = new ProgressBar { name = "analysis-progress", lowValue = 0, highValue = 1 }; _singlePage.Add(_progress);
            _comparison = new BundleComparisonView(_cache); Add(_comparison); ShowComparison(false);
            RefreshHistory(); ShowResources(false); SynchronizeSettings(); RefreshProgress();
            BundleAnalysisBuildQueue.Changed += BuildStateChanged;
            if (!BundleAnalysisBuildQueue.Running && BundleAnalysisBuildQueue.Current?.stage != BundleAnalysisBuildStage.Succeeded)
                RestoreCache();
            BuildStateChanged();
        }

        private static VisualElement Row() { var row = new VisualElement(); row.AddToClassList("analysis-toolbar"); return row; }
        private static Label Label(string id, string text = "") => new Label(text) { name = id, style = { whiteSpace = WhiteSpace.Normal } };
        private static MultiColumnListView List(string id) => new MultiColumnListView { name = id, fixedItemHeight = 38, selectionType = SelectionType.Single,
            virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly, style = { flexGrow = 1, minHeight = 80 } };
        private void Column(MultiColumnListView list, string id, string title, float width, Func<int, string> value, Action<int> action = null, string sort = null)
        {
            var column = new Column { name = id, title = title, width = width, minWidth = Math.Min(width, 70), stretchable = id == "name" || id == "issues" };
            column.makeCell = () => { var button = new Button(); button.AddToClassList("analysis-cell");
                button.clicked += () => { var index = (int)button.userData; if (action != null) action(index); else InspectPackage(_packRows[index]); }; return button; };
            column.bindCell = (cell, index) =>
            {
                cell.userData = index; ((Button)cell).text = value(index); cell.tooltip = value(index);
                cell.EnableInClassList("analysis-risk", list == _packs && id == "marks" && Session.Packages.Cyclic(_packRows[index]));
                cell.EnableInClassList("analysis-warning", list == _packs && id == "marks" && Session.Packages.SizeMark(_packRows[index], _workspace().Settings).Length > 0);
            };
            if (sort != null) column.makeHeader = () => new Button(() => { _descending = _sort == sort ? !_descending : true; _sort = sort; Rebuild(); }) { text = title + " ↕" };
            list.columns.Add(column);
        }

        internal void ImportReport(string path)
        {
            if (Running) throw new InvalidOperationException("请先取消当前检查。");
            BeginInspection(BundleBuildReport.Read(path));
        }
        private void BeginInspection(BundleBuildReport report)
        {
            if (Running) throw new InvalidOperationException("请先取消当前检查。");
            var settings = _workspace().ReadInspectionSettings(); var standards = JsonUtility.ToJson(settings.CurrentStandards);
            var next = new BundleInspectionSession(report, settings,
                () => _workspace().ConfigurationChanged || JsonUtility.ToJson(_workspace().Settings.CurrentStandards) != standards);
            BundleAnalysisBuildQueue.ClearResult();
            SaveCache();
            if (Session == null || Session.Packages.Report.Signature != report.Signature) _directory.SetValueWithoutNotify("");
            Session?.Dispose(); Session = next; _savedSession = null; _savedRecord = null; _cacheNotice = null; _owner = null; _inspector.Clear();
            ShowComparison(false); BindSource(); ShowResources(false); RefreshProgress(); _changed();
        }
        private void BindSource()
        {
            var report = Session.Packages.Report; var summary = report.Data.Summary;
            _path.SetValueWithoutNotify(report.SourcePath);
            _source.text = $"{summary.BuildPackageName} / {(BuildTarget)summary.BuildTarget} / {summary.BuildPackageVersion} / 构建于 {summary.BuildDate}\n完整报告范围；入包检查不应用目录范围或忽略名单；检查于 {Session.CheckedAt:yyyy/MM/dd HH:mm:ss}" +
                (Session.IsSnapshot ? "\n历史完整检查快照；重检资源按归档报告检查当前工程，不更新 AB 构建事实。" : "");
            _source.tooltip = report.SourcePath;
        }

        private BundleAnalysisViewState CaptureView() => new BundleAnalysisViewState
        {
            resources = _resourceView, kind = _kind.HasValue ? (int)_kind.Value : -1, directory = _directory.value,
            search = _search.value, filter = _filter.value, sort = _sort, descending = _descending,
            owner = _owner, package = _selectedPack, resource = _selectedResource == null ? null : BundlePackageAnalysis.Identity(_selectedResource.Asset),
            resourceSort = (int)_resources.Model.Sort, resourceDescending = _resources.Model.Descending, rule = _resources.Model.Rule,
            expanded = _resources.Model.Expanded.ToArray(), showUnknown = _resources.Model.ShowUnknown,
            openDetails = _inspector.Query<Foldout>().ToList().Where(foldout => foldout.value).Select(foldout => foldout.text).ToArray()
        };

        private void SaveCache()
        {
            if (Session == null || !Session.Complete) return;
            try
            {
                if (ReferenceEquals(_savedSession, Session)) _cache.SaveView(_savedRecord, CaptureView());
                else { _savedRecord = _cache.Save(Session, CaptureView()); _savedSession = Session; }
                RefreshHistory();
            }
            catch (Exception exception) when (BundleAnalysisCache.IsReadError(exception))
            { _cacheNotice = "本地历史保存失败：" + exception.Message; }
        }

        private void RestoreCache(string id = null)
        {
            try
            {
                var loaded = _cache.Load(id);
                if (loaded.Session == null) { _cacheNotice = _cache.Notice; return; }
                Session?.Dispose(); Session = loaded.Session; var view = loaded.View;
                _savedSession = Session; _savedRecord = _cache.Records().Single(record => record.reportSignature == Session.Packages.Report.Signature);
                _cacheNotice = _cache.Notice; ShowComparison(false);
                _directory.SetValueWithoutNotify(view.directory);
                BindSource(); ShowResources(view.resources);
                _kind = view.kind < 0 ? (AssetCheckKind?)null : (AssetCheckKind)view.kind;
                _sort = new[] { "size", "deps", "total" }.Contains(view.sort) ? view.sort : "size"; _descending = view.descending;
                _owner = view.owner != null && Session.Packages.Report.Bundles.ContainsKey(view.owner) ? view.owner : null;
                _search.SetValueWithoutNotify(view.search ?? ""); _filter.SetValueWithoutNotify(_filter.choices.Contains(view.filter) ? view.filter : "全部");
                Rebuild();
                if (_resourceView)
                {
                    _resources.RestoreView((AssetCheckSort)view.resourceSort, view.resourceDescending, view.rule, view.expanded, view.showUnknown);
                    var selected = Session.Resources.FirstOrDefault(resource => BundlePackageAnalysis.Identity(resource.Asset) == view.resource);
                    if (selected != null && _resources.Model.Files.Any(file => file.Id == view.resource)) InspectResource(selected);
                }
                else if (view.package != null && _packRows.Contains(view.package)) InspectPackage(view.package);
                foreach (var foldout in _inspector.Query<Foldout>().ToList()) foldout.value = view.openDetails.Contains(foldout.text);
                SaveCache(); RefreshProgress(); _changed();
            }
            catch (Exception exception) when (BundleAnalysisCache.IsReadError(exception))
            { _cacheNotice = "历史分析未恢复：" + exception.Message; RefreshProgress(); _changed(); }
        }

        private void RefreshHistory()
        {
            var selected = _history.value?.Id;
            var records = _cache.Records().ToList();
            _history.choices = records.Count == 0 ? new List<BundleAnalysisRecord> { null } : records;
            _history.SetValueWithoutNotify(records.FirstOrDefault(record => record.Id == selected) ??
                records.FirstOrDefault(record => record.reportSignature == Session?.Packages.Report.Signature) ?? records.FirstOrDefault());
            if (!string.IsNullOrEmpty(_cache.Notice)) _cacheNotice = _cache.Notice;
            RefreshHistoryControls();
        }
        private void RefreshHistoryControls()
        {
            _history.SetEnabled(!Running); _openHistory.SetEnabled(!Running && _history.value != null);
            _recheck.SetEnabled(!Running && Session != null && Session.IsSnapshot && !_workspace().ConfigurationChanged);
            _compareHistory.SetEnabled(!Running && _history.choices.Count(record => record != null) >= 2);
            _current.style.display = _comparing ? DisplayStyle.Flex : DisplayStyle.None;
            _comparison?.SetBusy(Running);
        }
        private void ShowComparison(bool comparing)
        {
            _comparing = comparing; _singlePage.style.display = comparing ? DisplayStyle.None : DisplayStyle.Flex;
            _comparison.style.display = comparing ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshHistoryControls();
        }

        private void BuildAndAnalyze()
        {
            if (Running) throw new InvalidOperationException("请先取消当前资源检查。");
            BundleAnalysisBuildQueue.Start(_target.value);
        }
        private void BuildStateChanged()
        {
            RunUi(() =>
            {
                var request = BundleAnalysisBuildQueue.Current;
                if (request != null) _target.SetValueWithoutNotify(request.target);
                var path = BundleAnalysisBuildQueue.TakeReport();
                if (path != null) { _path.SetValueWithoutNotify(path); ImportReport(path); }
                RefreshProgress(); _changed();
            });
        }
        private void RefreshBuildControls()
        {
            var active = EditorUserBuildSettings.activeBuildTarget;
            _activeTarget.text = "当前平台：" + active + (_target.value == active ? "；使用当前平台构建。" : "；将切换到 " + _target.value + "，编译完成后自动构建。");
            _target.SetEnabled(!Running && !_workspace().ConfigurationChanged);
            _build.SetEnabled(!Running && !_workspace().ConfigurationChanged && _target.choices.Count > 0);
        }

        internal void Pump()
        {
            if (Session == null || !Session.Running) return; var deadline = EditorApplication.timeSinceStartup + .008;
            do { Session.Step(); } while (Session.Running && EditorApplication.timeSinceStartup < deadline);
            if (Session.Complete) SaveCache();
            RefreshProgress();
            if (!Running && _selectedResource != null) InspectResource(_selectedResource);
            _changed();
        }
        internal void SynchronizeSettings()
        {
            var settings = _workspace().Settings; _minimum.SetValueWithoutNotify(settings.minBundleBytes / 1024); _maximum.SetValueWithoutNotify(settings.maxBundleBytes / 1048576f);
            Rebuild(); _read.SetEnabled(!Running && !_workspace().ConfigurationChanged); RefreshBuildControls(); _standards.SetEnabled(!Running && !_workspace().ConfigurationChanged);
            _configuration.style.display = _workspace().ConfigurationChanged ? DisplayStyle.Flex : DisplayStyle.None;
        }
        internal void ShowResources(bool resourceView)
        {
            _types.style.display = resourceView ? DisplayStyle.Flex : DisplayStyle.None;
            _directoryTools.style.display = resourceView ? DisplayStyle.Flex : DisplayStyle.None;
            _resourceView = resourceView; _selectedResource = null; _selectedPack = null; _owner = null; _packs.style.display = resourceView ? DisplayStyle.None : DisplayStyle.Flex; _resources.style.display = resourceView ? DisplayStyle.Flex : DisplayStyle.None;
            _packsTab.EnableInClassList("active", !resourceView); _resourcesTab.EnableInClassList("active", resourceView);
            _filter.choices = resourceView ? new List<string> { "全部", "需要关注", "违规", "优化候选", "未能检查", "未完成", "通过" } : new List<string> { "全部", "过大", "过小", "循环依赖" };
            _filter.SetValueWithoutNotify("全部"); _search.SetValueWithoutNotify(""); _inspector.Clear(); Rebuild();
        }
        private BundleReportBundleData Pack(string name) => Session.Packages.Report.Bundles[name];
        private static string Size(long bytes) => BundleAnalysisFormatting.Size(bytes);
        private string Marks(string name) => string.Join(" / ", new[] { Session.Packages.SizeMark(name, _workspace().Settings), Session.Packages.Cyclic(name) ? "循环依赖" : "" }.Where(s => s.Length > 0));
        internal void SelectResourceType(AssetCheckKind? kind)
        {
            _kind = kind; _selectedResource = null; _inspector.Clear(); Rebuild();
        }

        private BundleResourceResult Resource(string id) => Session.Resources.First(result => BundlePackageAnalysis.Identity(result.Asset) == id);

        private AssetCheckFileData Display(BundleResourceResult result)
        {
            var size = Session.Packages.ResourceSize(result.Asset, _owner);
            var check = _kind.HasValue ? result.Checks.Single(item => item.Kind == _kind.Value) : null;
            var issues = check == null ? result.Issues.ToArray() : check.Issues.ToArray();
            var complete = (check?.Complete ?? result.Checked) && result.ContentChecked;
            var unverified = result.Notes.Count > 0 || issues.Any(issue => issue.Unverified);
            var problem = issues.Any(issue => issue.HasProblem);
            var status = problem ? unverified ? "违规／含未能验证" : "违规" : unverified ? "未能检查" : result.Candidate ? "优化候选" : complete ? "通过" : "未完成";
            if (result.Candidate && (problem || unverified)) status += "／优化候选";
            if (!complete && status != "未完成") status += "／未完成";
            var note = string.Join("；", result.Notes.Concat(result.Candidate ? new[] { result.SameContent.Length > 1 ? "不同文件源内容相同" : "同资源跨包收录" } : Array.Empty<string>()));
            return new AssetCheckFileData { Id = BundlePackageAnalysis.Identity(result.Asset), Path = result.Asset.Path, Kind = _kind,
                Issues = issues, Complete = complete, Unverified = unverified, Candidate = result.Candidate, Status = status, Note = note,
                SizeBytes = size.Coverage == AssetBundleSizeCoverage.Unknown ? (long?)null : size.Bytes, SizePartial = size.Coverage == AssetBundleSizeCoverage.Partial,
                SizeNote = "包内数据大小（未压缩），不含独立依赖资源；部分统计排在完整统计之后。" + size.Reason,
                ResolvePath = result.Asset.ResolveCurrentPath };
        }

        internal void SetResourceDirectory(string directory)
        {
            _directory.SetValueWithoutNotify(BundleResourceDirectoryFilter.Normalize(directory));
            RefreshProgress();
        }

        private void RebuildResources()
        {
            var scoped = Session?.Resources.Where(result => (_owner == null || result.Asset.Owners.Contains(_owner)) &&
                BundleResourceDirectoryFilter.Contains(_directory.value, result.Asset.Path)).ToArray() ?? Array.Empty<BundleResourceResult>();
            _resourceSizeColumn.title = _owner == null ? "合计入包大小" : "本包大小";
            _types.Select(_kind); _unclassified.EnableInClassList("active", !_kind.HasValue);
            foreach (var registration in AssetCheckCatalog.Entries)
            {
                var kind = registration.Kind;
                var checks = scoped.SelectMany(result => result.Checks).Where(check => check.Kind == kind).ToArray();
                var problems = checks.Count(check => check.Issues.Any(issue => issue.HasProblem));
                _types.Bind(kind, Session == null ? "—" : problems.ToString(),
                    $"当前目录及所属包筛选内：{checks.Length} 个适用资源，规则完成 {checks.Count(check => check.Complete)}；问题 {problems}。文件可属于多个分类，分类数量不能相加作为总数。",
                    Session != null && Session.ClassificationReady && checks.All(check => check.Complete) && problems == 0,
                    !registration.ProjectWide || checks.Length > 0);
            }
            var unclassified = scoped.Count(result => result.Checks.Count == 0);
            _unclassified.text = "未归类 " + unclassified; _unclassified.tooltip = "缺失或没有适用检查模块的资源；准备分类期间还包含待分类资源。";
            var candidates = scoped.Where(result => _kind.HasValue ? result.Checks.Any(check => check.Kind == _kind.Value) : result.Checks.Count == 0).ToArray();
            var files = candidates.Select(Display).ToArray();
            _resources.BindFiles(Session, (_kind?.ToString() ?? "unclassified") + "|" + _owner + "|" + _directory.value, Session?.Revision ?? -1, files, Running);
            _resources.Query(_search.value, _filter.value);
            if (Session == null) return;
            _counts.tooltip = "目录按报告记录路径（含子目录）与所属 AB 取交集；分类统计在此范围内，当前显示数量另计搜索、状态和问题类型筛选。";
            _counts.text = $"全报告 {Session.Resources.Length} 个资源；当前目录 {scoped.Length}；当前分类 {files.Length}；当前显示 {_resources.Model.Files.Count}；规则完成 {candidates.Count(result => _kind.HasValue ? result.Checks.Single(check => check.Kind == _kind.Value).Complete : result.Checked)}，内容完成 {candidates.Count(result => result.ContentChecked)}；问题 {files.Count(file => file.Issues.Any(issue => issue.HasProblem))}，候选 {files.Count(file => file.Candidate)}，未能检查 {files.Count(file => file.Unverified)}" + (_owner == null ? "" : "；所属包：" + _owner);
        }

        private void LocateResource(AssetCheckFileRow file, AssetCheckIssue issue)
        {
            var result = Resource(file.Id); InspectResource(result);
            var path = result.Asset.ResolveCurrentPath();
            if (issue != null && path.Length > 0 && AssetDatabase.AssetPathToGUID(path) == issue.TargetId) AssetCheckLocator.Locate(issue, true);
            else Locate(result.Asset);
        }

        private void Rebuild()
        {
            if (Session == null) { _packRows.Clear(); _summary.text = "选择已有 report 后开始分析。"; _counts.text = ""; }
            else
            {
                var search = _search.value ?? ""; var filter = _filter.value; var packages = Session.Packages;
                var names = packages.Report.Bundles.Keys.Where(n => n.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 && (_resourceView || filter == "全部" || Marks(n).Contains(filter)));
                Func<string, long> key = n => _sort == "deps" ? packages.Dependencies(n).Length : _sort == "total" ? packages.TotalBytes(n) : Pack(n).FileSize;
                _packRows = (_descending ? names.OrderByDescending(key) : names.OrderBy(key)).ThenBy(n => n, StringComparer.Ordinal).ToList();
                _summary.text = $"AB：{packages.Report.Bundles.Count} 包 / {Size(packages.Report.TotalBytes)}；过大 {packages.Report.Bundles.Keys.Count(n => packages.SizeMark(n, _workspace().Settings) == "过大")}，过小 {packages.Report.Bundles.Keys.Count(n => packages.SizeMark(n, _workspace().Settings) == "过小")}，循环依赖 {packages.Report.Bundles.Keys.Count(packages.Cyclic)} 包。";
                _counts.text = $"显示 {_packRows.Count} 包；依赖数不含自身，总计按包去重；报告关系可能包含间接依赖。";
            }
            _rebinding = true;
            try
            {
                _packs.itemsSource = _packRows; _packs.RefreshItems();
                var packageIndex = _selectedPack == null ? -1 : _packRows.IndexOf(_selectedPack);
                _packs.SetSelectionWithoutNotify(packageIndex < 0 ? Array.Empty<int>() : new[] { packageIndex });
                if (!_resourceView && _selectedPack != null && packageIndex < 0)
                { _selectedResource = null; _selectedPack = null; _inspector.Clear(); }
            }
            finally { _rebinding = false; }
            if (_resourceView) RebuildResources();
        }
        private void RefreshProgress()
        {
            var build = BundleAnalysisBuildQueue.Current;
            _status.text = build == null ? Session == null ? "尚未分析" : Session.Activity + (Session.Failure == null ? "" : "：" + Session.Failure) :
                build.stage == BundleAnalysisBuildStage.Failed ? "分析构建失败：" + build.error :
                build.stage == BundleAnalysisBuildStage.Cancelled ? "已取消后续构建；已开始的平台切换仍可能完成。" :
                build.stage == BundleAnalysisBuildStage.Building ? "正在构建分析 AB：" + build.target : "等待平台切换、脚本编译及重载完成：" + build.target;
            if (!string.IsNullOrEmpty(_cacheNotice)) _status.text += "；" + _cacheNotice;
            _status.tooltip = _status.text; RefreshHistoryControls();
            _cancel.text = build != null && build.Running ? "取消后续构建" : "取消检查";
            _cancel.SetEnabled(build == null || !build.Running || build.CanCancel);
            _cancel.style.display = Running ? DisplayStyle.Flex : DisplayStyle.None; _progress.style.display = Running ? DisplayStyle.Flex : DisplayStyle.None; _progress.value = Session?.Progress ?? 0;
            _path.SetEnabled(!Running); _chooseReport.SetEnabled(!Running); _read.SetEnabled(!Running && !_workspace().ConfigurationChanged); RefreshBuildControls(); _standards.SetEnabled(!Running && !_workspace().ConfigurationChanged);
            if (!Running || EditorApplication.timeSinceStartup >= _nextResultsRefresh)
            { _nextResultsRefresh = EditorApplication.timeSinceStartup + .1; Rebuild(); }
        }
        private void Heading(string text) { var label = Label("", text); label.AddToClassList("analysis-section-title"); _inspector.Add(label); }
        private void Line(string text) => _inspector.Add(Label("", text));
        private static Button Link(string text, Action action) { var button = new Button(action) { text = text }; button.AddToClassList("analysis-detail-item"); return button; }
        private void AssetLink(VisualElement parent, BundleAnalysisAsset asset, string prefix = "") { var button = Link(prefix + Path.GetFileName(asset.Path), () => Locate(asset)); button.tooltip = asset.Path; parent.Add(button); }
        private void Locate(BundleAnalysisAsset asset)
        {
            var path = asset.ResolveCurrentPath(); if (string.IsNullOrEmpty(path)) { _status.text = "当前工程未匹配，无法定位：" + asset.Path; return; }
            AssetCheckLocator.Locate(path); _status.text = "已定位：" + path;
        }
        internal void InspectPackage(string name)
        {
            _selectedResource = null; _selectedPack = name; var selectedIndex = _packRows.IndexOf(name);
            _packs.SetSelectionWithoutNotify(selectedIndex < 0 ? Array.Empty<int>() : new[] { selectedIndex });
            _inspector.Clear(); var packages = Session.Packages; Heading(name);
            Line($"自身：{Size(Pack(name).FileSize)}；依赖：{packages.Dependencies(name).Length} 包 / {Size(packages.TotalBytes(name) - Pack(name).FileSize)}；含依赖：{Size(packages.TotalBytes(name))}");
            if (packages.Cyclic(name))
            {
                var foldout = new Foldout { name = "analysis-cycle", text = "循环依赖：展开查看路径及致因", value = false }; var populated = false;
                foldout.RegisterValueChangedCallback(e => { if (!e.newValue || populated) return; populated = true;
                    var path = packages.CyclePath(name); foldout.Add(Label("cycle-path", string.Join(" → ", path)));
                    for (var i = 0; i + 1 < path.Length; i++)
                    {
                        foldout.Add(Label("", path[i] + " → " + path[i + 1])); var causes = packages.Causes(path[i], path[i + 1]);
                        if (causes.Length == 0) foldout.Add(Label("", "报告未提供可定位的资源致因；不以当前引用冒充历史依据。"));
                        foreach (var cause in causes) { foldout.Add(Label("", "报告资源依赖（可能间接）：" + cause.Source.Path + " → " + cause.Dependency.Path)); AssetLink(foldout, cause.Source, "定位来源："); AssetLink(foldout, cause.Dependency, "定位依赖："); }
                    }
                }); _inspector.Add(foldout);
            }
            Heading("依赖包（去重）"); foreach (var dependency in packages.Dependencies(name)) { var id = dependency; _inspector.Add(Link(id + " / " + Size(Pack(id).FileSize), () => InspectPackage(id))); }
            var references = new Foldout { text = "谁依赖此包（报告关系）", value = false }; foreach (var reference in packages.Analysis.References(name)) { var id = reference; references.Add(Link(id, () => InspectPackage(id))); } _inspector.Add(references);
            var contents = new Foldout { text = "包内资源（未压缩大小，从大到小）", value = false };
            var items = Session.Resources.Where(r => r.Asset.Owners.Contains(name)).Select(r => (Result: r, Size: packages.ResourceSize(r.Asset, name)))
                .OrderBy(r => r.Size.Coverage == AssetBundleSizeCoverage.Complete ? 0 : r.Size.Coverage == AssetBundleSizeCoverage.Partial ? 1 : 2)
                .ThenByDescending(r => r.Size.Bytes).ThenBy(r => r.Result.Asset.Path, StringComparer.Ordinal);
            foreach (var entry in items)
            {
                var item = entry.Result; var row = new VisualElement(); row.AddToClassList("analysis-resource-row");
                var button = Link(Path.GetFileName(item.Asset.Path) + " / " + ResourceSizeCaption(entry.Size), () => Locate(item.Asset));
                button.name = "analysis-resource-locate"; button.tooltip = "选中并 Ping 资源：" + item.Asset.Path + "\n" + entry.Size.Reason; row.Add(button);
                var checks = Link("查看检查结果", () => ShowResourceChecks(name, item));
                checks.name = "analysis-resource-checks"; checks.tooltip = "转到此资源的检查结果，清除目录、搜索、状态和问题类型筛选。"; row.Add(checks);
                contents.Add(row);
            }
            _inspector.Add(contents);
            if (packages.Report.Sizes != null)
            {
                var written = packages.Report.Sizes.Bundle(name);
                var generated = new Foldout { text = "生成数据与结构占用", value = false };
                generated.Add(Label("", "未压缩写出数据：" + Size(written.UnpackedBytes) + "；结构及未归属数据：" + Size(written.UnassignedBytes)));
                foreach (var data in written.Generated) generated.Add(Label("", data.Description + " / " + Size(data.Bytes)));
                _inspector.Add(generated);
            }
            else Line("此报告未包含逐资源大小；可通过“构建 AB 并分析”生成对应统计。");
            _inspector.Add(Link("查看此包的资源检查结果", () => { ShowResources(true); _owner = name; Rebuild(); }));
        }
        private void ShowResourceChecks(string owner, BundleResourceResult result)
        {
            _directory.SetValueWithoutNotify(""); ShowResources(true); _owner = owner;
            SelectResourceType(result.Checks.Count == 0 ? (AssetCheckKind?)null : result.Checks[0].Kind);
            _resources.RevealFile(BundlePackageAnalysis.Identity(result.Asset)); InspectResource(result);
        }
        internal void InspectResource(BundleResourceResult result)
        {
            _selectedResource = result; _selectedPack = null;
            _inspector.Clear(); Heading(Path.GetFileName(result.Asset.Path)); Line(result.Asset.Path);
            Line((_owner == null ? "合计入包大小（未压缩）：" : "本包大小（未压缩）：") + ResourceSizeCaption(Session.Packages.ResourceSize(result.Asset, _owner)));
            Line("资源整体（所有检查分类）：" + result.Status);
            Line(!result.Checked ? "检查时匹配：尚未完成" : string.IsNullOrEmpty(result.CheckedPath) ? "检查时匹配：未匹配当前资源" :
                BundlePackageAnalysis.Identity(result.Asset).StartsWith("guid:", StringComparison.Ordinal) ? "检查时匹配：GUID" : "检查时匹配：路径（报告无 GUID）");
            if (!result.ContentChecked) Line("源文件内容重复检查尚未完成；不能据此判断没有重复内容。");
            Line("当前配置检查快照：" + Session.CheckedAt.ToString("yyyy/MM/dd HH:mm:ss")); AssetLink(_inspector, result.Asset, "定位资源：");
            foreach (var note in result.Notes) Line(note);
            if (result.Asset.Owners.Count > 1) Line("优化候选：同一资源在多个包分别收录；不估算节省字节。");
            if (result.SameContent.Length > 1) { Heading("不同文件源内容相同（导入配置和用途仍需核对）"); foreach (var path in result.SameContent) AssetLink(_inspector, Session.Resources.First(r => r.CheckedPath == path).Asset); }
            Heading("所属 AB"); foreach (var owner in result.Asset.Owners) { var id = owner; _inspector.Add(Link(id + " / " + ResourceSizeCaption(Session.Packages.ResourceSize(result.Asset, id)), () => { ShowResources(false); InspectPackage(id); })); }
            Line("修改后的资源复查请使用资源检查；AB 变化请读取新构建报告。");
        }
        private static string ResourceSizeCaption(AssetBundleResourceSize size) => ResourceSizeCaption(size.Coverage == AssetBundleSizeCoverage.Unknown ? (long?)null : size.Bytes, size.Coverage == AssetBundleSizeCoverage.Partial);
        private static string ResourceSizeCaption(long? bytes, bool partial) => !bytes.HasValue ? "未统计" : partial ? "已统计 " + Size(bytes.Value) + "（部分）" : Size(bytes.Value);
        private void RunUi(Action action)
        {
            try { action(); SynchronizeSettings(); }
            catch (Exception exception) { _status.text = "分析失败：" + exception.Message; _cancel.style.display = Running ? DisplayStyle.Flex : DisplayStyle.None; _changed(); JLogger.LogException(exception); }
        }
        public void Dispose() { BundleAnalysisBuildQueue.Changed -= BuildStateChanged; SaveCache(); Session?.Dispose(); }
    }
}
