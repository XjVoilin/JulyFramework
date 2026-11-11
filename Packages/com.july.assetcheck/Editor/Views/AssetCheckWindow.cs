using July.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    public sealed class AssetCheckWindow : EditorWindow
    {
        internal enum ToolPage { ResourceCheck, PlatformCopy, BundleAnalysis, FontCheck, PlayerBuildAnalysis }
        private AssetCheckWorkspace _workspace;
        private AssetCheckKind _kind;
        private ObjectField _scope;
        private VisualElement _checkPage;
        private VisualElement _scopeHeader;
        private WebGLTextureSettingsCopyPanel _copyPage;
        private BundleAnalysisPanel _analysisPage;
        private FontCheckPanel _fontPage;
        private PlayerBuildAnalysisPanel _playerBuildPage;
        private Button _playerBuildTool;
        private Button _fontTool;
        private Button _checkTool, _copyTool, _analysisTool;
        [SerializeField] private ToolPage _tool;
        private AssetCheckOverview _overview;
        private Label _status;
        private Label _error;
        private Label _notice;
        private ProgressBar _progress;
        private Foldout _standards;
        private Foldout _ignoredFoldout;
        private AssetCheckResultsView _results;
        private ListView _ignored;
        private AssetCheckExclusions _ignoredExclusions;
        private AssetCheckTypeTabs _typeTabs;
        private Button _fixSelected;
        private Button _scanAll;
        private Button _scanCurrent;
        private Button _fixAll;
        private Button _selection;
        private Button _allAssets;
        private ToolbarMenu _settingsMenu;
        private Button _cancel;
        private bool _refreshRequested;
        private double _nextRefresh, _nextPoll;
        private string SessionKey => "July.AssetCheck." + Application.dataPath;

        [MenuItem("JulyGF/资源管理/资源工具", priority = 64)]
        public static void Open()
        {
            var window = GetWindow<AssetCheckWindow>("资源工具");
            window.minSize = new Vector2(1050, 600);
            window.Show();
        }

        // 与工具栏共用检查入口，供编辑器自动化和集成测试调用。
        public void CheckAll() => RunUi(() =>
        {
            RequireIdle();
            _tool = ToolPage.ResourceCheck;
            _workspace.StartScan();
        });

        internal bool FolderOperationRunning => (_workspace != null && _workspace.Running) || (_copyPage != null && _copyPage.Running) || (_analysisPage != null && _analysisPage.Running) || (_fontPage != null && _fontPage.Running);
        internal void SetFoldersIgnored(string[] paths, bool ignored) => RunUi(() => _workspace.SetFoldersIgnored(paths, ignored));

        private void OnEnable()
        {
            EditorApplication.update += Pump;
            _kind = (AssetCheckKind)SessionState.GetInt(SessionKey + ".tab", 0);
        }

        private void OnDisable()
        {
            EditorApplication.update -= Pump;
            _fontPage?.Dispose();
            _fontPage = null;
            _analysisPage?.Dispose();
            _analysisPage = null;
            _copyPage?.Dispose();
            _copyPage = null;
            if (_workspace != null)
            {
                _workspace.Changed -= RequestRefresh;
                _workspace.Dispose();
                _workspace = null;
            }
        }

        private void Pump()
        {
            if (_workspace == null) return;
            var now = EditorApplication.timeSinceStartup;
            if (now >= _nextPoll)
            {
                _nextPoll = now + 0.5;
                _workspace.PollExternalChanges();
            }
            if (_workspace.Running)
            {
                // 增量规则可能在文件内部让出执行权；采用较小的单帧时间预算，
                // 避免检查进度依赖编辑器前台帧率。
                var deadline = EditorApplication.timeSinceStartup + 0.008;
                do { _workspace.Step(); }
                while (_workspace.Running && EditorApplication.timeSinceStartup < deadline);
            }
            _copyPage?.Pump();
            _analysisPage?.Pump();
            _fontPage?.Pump();
            RefreshSharedControls();
            if (_refreshRequested && now >= _nextRefresh)
            {
                _nextRefresh = now + 0.1;
                Refresh();
            }
        }

        public void CreateGUI()
        {
            _fontPage?.Dispose();
            _fontPage = null;
            _analysisPage?.Dispose();
            _analysisPage = null;
            _copyPage?.Dispose();
            _copyPage = null;
            var shell = rootVisualElement;
            shell.Clear();
            shell.AddToClassList("asset-check");
            shell.EnableInClassList("theme-dark", EditorGUIUtility.isProSkin);
            shell.EnableInClassList("theme-light", !EditorGUIUtility.isProSkin);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.july.assetcheck/Editor/Views/AssetCheckWindow.uss");
            shell.styleSheets.Add(sheet);
            BuildSharedHeader(shell);
            _checkPage = new VisualElement { name = "resource-check-page", style = { flexGrow = 1 } };
            shell.Add(_checkPage);
            var root = _checkPage;

            var toolbar = new VisualElement();
            toolbar.AddToClassList("header");
            var platform = new PopupField<string>("平台", new List<string> { "小游戏" }, 0)
            {
                tooltip = "小游戏统一检查 WebGL、MiniGame、WeixinMiniGame 设置。"
            };
            platform.AddToClassList("platform");
            toolbar.Add(platform);
            platform.style.flexGrow = 1;
            _scanAll = new Button(CheckAll) { text = "检查全部" };
            _scanAll.AddToClassList("header-action");
            _scanAll.AddToClassList("primary");
            toolbar.Add(_scanAll);
            root.Add(toolbar);

            _typeTabs = new AssetCheckTypeTabs(SelectTab); root.Add(_typeTabs);

            _overview = new AssetCheckOverview();
            root.Add(_overview);

            _standards = new Foldout { value = SessionState.GetBool(SessionKey + ".standards", false) };
            _standards.AddToClassList("standards");
            _standards.RegisterValueChangedCallback(change => SessionState.SetBool(SessionKey + ".standards", change.newValue));
            root.Add(_standards);

            var reportBar = new VisualElement();
            reportBar.AddToClassList("report-bar");
            _scanCurrent = new Button(() => RunUi(() => _workspace.StartScan(_kind)));
            reportBar.Add(_scanCurrent);
            _fixSelected = new Button(() => RunUi(() => _workspace.StartFix(_kind,
                _results.SelectedFixes))) { text = "修改选中" };
            reportBar.Add(_fixSelected);
            _fixAll = new Button(() => RunUi(() => _workspace.StartFix(_kind, _results.AllFixes)))
            {
                name = "fix-all", text = "修改全部",
                tooltip = "修改当前类型全部已确认问题，不受筛选和折叠影响；未验证目标需明确设为标准。"
            };
            reportBar.Add(_fixAll);
            _notice = new Label("ⓘ") { tooltip = "修改范围与操作说明" };
            _notice.AddToClassList("help");
            reportBar.Add(_notice);
            root.Add(reportBar);

            _results = new AssetCheckResultsView(
                issues => RunUi(() => _workspace.StartFix(_kind, issues)),
                issue => RunUi(() => _workspace.Ignore(issue)),
                (issue, precise) => RunUi(() =>
                {
                    if (precise) AssetCheckLocator.Locate(issue, CanModify(_workspace.GetReport(_kind)));
                    else AssetCheckLocator.Locate(AssetCheckTarget.Resolve(issue.TargetId));
                }));
            _results.SelectionChanged += RefreshSelection;
            root.Add(_results);

            _ignoredFoldout = new Foldout { value = SessionState.GetBool(SessionKey + ".ignored", false) };
            _ignoredFoldout.AddToClassList("ignored-section");
            _ignoredFoldout.tooltip = "文件夹一条记录可覆盖多个文件；概览中的忽略数量按本次扫描实际跳过的文件统计。恢复只移除对应记录，不清除其它独立忽略。";
            _ignoredFoldout.RegisterValueChangedCallback(change => SessionState.SetBool(SessionKey + ".ignored", change.newValue));
            _ignored = new ListView { selectionType = SelectionType.None, fixedItemHeight = 28, makeItem = MakeIgnoredRow };
            _ignored.bindItem = BindIgnoredRow;
            _ignoredFoldout.Add(_ignored);
            root.Add(_ignoredFoldout);

            _error = new Label();
            _error.AddToClassList("operation-error");
            var footer = new VisualElement();
            footer.AddToClassList("footer");
            _status = new Label();
            _status.AddToClassList("summary");
            footer.Add(_status);
            _cancel = new Button(() => _workspace.Cancel()) { text = "取消" };
            footer.Add(_cancel);
            root.Add(footer);
            _progress = new ProgressBar { lowValue = 0, highValue = 1 };
            root.Add(_progress);

            RunUi(LoadWorkspace);
            if (_workspace != null)
            {
                _copyPage = new WebGLTextureSettingsCopyPanel(() => _workspace, CopyStateChanged);
                shell.Add(_copyPage);
                _analysisPage = new BundleAnalysisPanel(() => _workspace, RequestRefresh);
                shell.Add(_analysisPage);
                _fontPage = new FontCheckPanel(() => _workspace, RequestRefresh);
                shell.Add(_fontPage);
                _playerBuildPage = new PlayerBuildAnalysisPanel();
                shell.Add(_playerBuildPage);
                if (_tool == ToolPage.FontCheck) _fontPage.Activate();
            }
            shell.Add(_error);
            Refresh();
        }

        private void BuildSharedHeader(VisualElement shell)
        {
            var header = new VisualElement();
            _scopeHeader = header;
            header.AddToClassList("header");
            _scope = new ObjectField("目录") { name = "shared-scope", objectType = typeof(DefaultAsset), allowSceneObjects = false };
            _scope.AddToClassList("scope");
            _scope.labelElement.style.minWidth = 36;
            _scope.RegisterValueChangedCallback(change => RunUi(() => SetScope(AssetDatabase.GetAssetPath(change.newValue))));
            header.Add(_scope);
            _selection = new Button(() => RunUi(UseSelectedFolder)) { text = "使用选中目录" };
            _selection.AddToClassList("header-action");
            header.Add(_selection);
            _allAssets = new Button(() => RunUi(() => SetScope("Assets"))) { text = "全部 Assets" };
            _allAssets.AddToClassList("header-action");
            header.Add(_allAssets);
            shell.Add(header);
            var tools = new Toolbar();
            _checkTool = new Button(() => SelectTool(ToolPage.ResourceCheck)) { name = "tool-check", text = "资源检查" };
            _copyTool = new Button(() => SelectTool(ToolPage.PlatformCopy)) { name = "tool-copy", text = "平台配置复制" };
            tools.Add(_checkTool);
            tools.Add(_copyTool);
            _analysisTool = new Button(() => SelectTool(ToolPage.BundleAnalysis)) { name = "tool-analysis", text = "打包分析" };
            tools.Add(_analysisTool);
            _playerBuildTool = new Button(() => SelectTool(ToolPage.PlayerBuildAnalysis)) { name = "tool-player-build", text = "构建资源大小" };
            tools.Add(_playerBuildTool);
            _fontTool = new Button(() => SelectTool(ToolPage.FontCheck)) { name = "tool-font", text = "字体检查" };
            tools.Add(_fontTool);
            tools.Add(new VisualElement { style = { flexGrow = 1 } });
            _settingsMenu = new ToolbarMenu { name = "shared-settings", text = "设置", tooltip = "配置文件、本地报告与重新加载" };
            _settingsMenu.AddToClassList("settings-action");
            _settingsMenu.menu.AppendAction("重新加载配置", _ => RunUi(LoadWorkspace));
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var configPath = Path.Combine(projectRoot, AssetCheckSettingsFile.FileName);
            var reportsPath = Path.Combine(projectRoot, AssetCheckReportStore.RelativeDirectory);
            _settingsMenu.menu.AppendAction("定位项目配置", _ => EditorUtility.RevealInFinder(configPath),
                _ => File.Exists(configPath) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            _settingsMenu.menu.AppendAction("打开本地报告目录", _ => EditorUtility.RevealInFinder(reportsPath),
                _ => Directory.Exists(reportsPath) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            _settingsMenu.tooltip += "\n项目配置：" + configPath + "\n本地报告：" + reportsPath;
            tools.Add(_settingsMenu);
            shell.Add(tools);
        }

        internal void SelectTool(ToolPage tool)
        {
            RequireIdle();
            _tool = tool;
            if (tool == ToolPage.FontCheck) _fontPage?.Activate();
            if (tool == ToolPage.PlayerBuildAnalysis) _playerBuildPage?.Activate();
            RefreshSharedControls();
        }

        internal void SetScope(string path)
        {
            RequireIdle();
            AssetCheckSettings.ValidateScope(path);
            if (!AssetDatabase.IsValidFolder(path))
                throw new InvalidOperationException("请选择 Assets 下的文件夹。");
            _workspace.ChangeSettings(settings => settings.scope = path);
            Refresh();
        }

        private void RequireIdle()
        {
            if (FolderOperationRunning)
                throw new InvalidOperationException("请等待当前操作完成，或先取消。");
        }

        private void CopyStateChanged()
        {
            RefreshSharedControls();
            RequestRefresh();
        }

        private void RefreshSharedControls()
        {
            var ready = _workspace != null;
            var idle = ready && !FolderOperationRunning;
            _scopeHeader.style.display = _tool == ToolPage.BundleAnalysis || _tool == ToolPage.FontCheck || _tool == ToolPage.PlayerBuildAnalysis ? DisplayStyle.None : DisplayStyle.Flex;
            _scope.SetEnabled(idle);
            _selection.SetEnabled(idle);
            _allAssets.SetEnabled(idle);
            _checkTool.SetEnabled(idle);
            _copyTool.SetEnabled(idle && _copyPage != null);
            _analysisTool.SetEnabled(idle && _analysisPage != null);
            _fontTool.SetEnabled(idle && _fontPage != null);
            _playerBuildTool.SetEnabled(idle && _playerBuildPage != null);
            _checkTool.style.unityFontStyleAndWeight = _tool == ToolPage.ResourceCheck ? FontStyle.Bold : FontStyle.Normal;
            _copyTool.style.unityFontStyleAndWeight = _tool == ToolPage.PlatformCopy ? FontStyle.Bold : FontStyle.Normal;
            _analysisTool.style.unityFontStyleAndWeight = _tool == ToolPage.BundleAnalysis ? FontStyle.Bold : FontStyle.Normal;
            _fontTool.style.unityFontStyleAndWeight = _tool == ToolPage.FontCheck ? FontStyle.Bold : FontStyle.Normal;
            _playerBuildTool.style.unityFontStyleAndWeight = _tool == ToolPage.PlayerBuildAnalysis ? FontStyle.Bold : FontStyle.Normal;
            _checkPage.style.display = _tool == ToolPage.ResourceCheck || _copyPage == null ? DisplayStyle.Flex : DisplayStyle.None;
            _checkPage.SetEnabled(idle);
            if (_copyPage != null)
            {
                _copyPage.style.display = _tool == ToolPage.PlatformCopy ? DisplayStyle.Flex : DisplayStyle.None;
                _copyPage.SetEnabled(ready && !_workspace.Running && (_analysisPage == null || !_analysisPage.Running) && (_fontPage == null || !_fontPage.Running));
            }
            if (_analysisPage != null)
            {
                _analysisPage.style.display = _tool == ToolPage.BundleAnalysis ? DisplayStyle.Flex : DisplayStyle.None;
                _analysisPage.SetEnabled(ready && !_workspace.Running && (_copyPage == null || !_copyPage.Running) && (_fontPage == null || !_fontPage.Running));
            }
            if (_playerBuildPage != null)
            {
                _playerBuildPage.style.display = _tool == ToolPage.PlayerBuildAnalysis ? DisplayStyle.Flex : DisplayStyle.None;
                _playerBuildPage.SetEnabled(idle);
            }
            if (_fontPage != null)
            {
                _fontPage.style.display = _tool == ToolPage.FontCheck ? DisplayStyle.Flex : DisplayStyle.None;
                _fontPage.SetEnabled(ready && !_workspace.Running && (_copyPage == null || !_copyPage.Running) && (_analysisPage == null || !_analysisPage.Running));
            }
        }

        private void LoadWorkspace()
        {
            var file = new AssetCheckSettingsFile(Directory.GetParent(Application.dataPath).FullName);
            var next = new AssetCheckWorkspace(file);
            if (_workspace != null)
            {
                _workspace.Changed -= RequestRefresh;
                _workspace.Dispose();
            }
            _workspace = next;
            _workspace.Changed += RequestRefresh;
            BuildStandards();
        }

        private void SelectTab(AssetCheckKind kind)
        {
            _kind = kind;
            SessionState.SetInt(SessionKey + ".tab", (int)kind);
            BuildStandards();
            Refresh();
        }

        private void BuildStandards()
        {
            var offset = _standards.Q<ScrollView>()?.scrollOffset ?? Vector2.zero;
            _standards.contentContainer.Clear();
            if (_workspace == null)
                return;
            _standards.text = "检查标准（" + AssetCheckWorkspace.KindName(_kind) + "）";
            var form = (ScrollView)AssetCheckStandardsView.Build(_kind, _workspace.Settings, Change);
            _standards.Add(form);
            form.schedule.Execute(() => form.scrollOffset = offset);
        }

        private void Change(Action<AssetCheckSettings> change)
        {
            RunUi(() => _workspace.ChangeSettings(change));
            BuildStandards();
            Refresh();
        }

        private void UseSelectedFolder()
        {
            SetScope(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        private VisualElement MakeIgnoredRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ignored-row");
            var label = new Label { name = "path" };
            label.AddToClassList("summary");
            row.Add(label);
            row.Add(new Button(() => RunUi(() => _workspace.Restore(((IgnoredAsset)row.userData).Id)))
                { name = "restore", text = "恢复检查" });
            row.Add(new Button(() => AssetCheckLocator.Locate(AssetCheckTarget.Resolve(((IgnoredAsset)row.userData).Id)))
                { name = "locate", text = "定位" });
            return row;
        }

        private void BindIgnoredRow(VisualElement row, int index)
        {
            var record = (IgnoredAsset)_ignored.itemsSource[index];
            row.userData = record;
            var path = AssetCheckTarget.Resolve(record.Id);
            var parent = _ignoredExclusions.FindParentFolder(path);
            var folder = AssetDatabase.IsValidFolder(path);
            var label = row.Q<Label>("path");
            label.text = string.IsNullOrEmpty(path) ? Path.GetFileName(record.lastKnownPath) + "（资源已移除）" :
                (folder ? "文件夹 · " : "") + Path.GetFileName(path) +
                (parent != null ? "（仍被上级目录忽略）" : "") + " · " + Path.GetDirectoryName(path);
            label.tooltip = (string.IsNullOrEmpty(path) ? record.lastKnownPath : path) +
                (folder ? "\n此目录及其子目录中的资源不参与扫描；恢复不会清除单文件或子目录的独立忽略记录。" : "") +
                (parent != null ? "\n上级忽略来源：" + parent : "");
            var restore = row.Q<Button>("restore");
            restore.text = parent != null || string.IsNullOrEmpty(path) ? "移除记录" : "恢复检查";
            restore.tooltip = parent != null ? "只移除此条独立记录；仍由上级目录 " + parent + " 忽略。" :
                string.IsNullOrEmpty(path) ? "清理已移除资源的忽略记录。" : "移除此条忽略记录，重新扫描后恢复检查；其它独立忽略记录保留。";
            restore.SetEnabled(!_workspace.Running);
            row.Q<Button>("locate").SetEnabled(!string.IsNullOrEmpty(path));
        }

        private void RunUi(Action action)
        {
            try
            {
                action();
                _error.text = string.Empty;
            }
            catch (Exception exception)
            {
                // 用户、文件和 Unity 操作的边界：明确展示错误，并保留原有数据。
                _error.text = exception.Message;
                JLogger.LogException(exception);
            }
            Refresh();
        }

        private void RequestRefresh() => _refreshRequested = true;

        private void Refresh()
        {
            _error.style.display = string.IsNullOrEmpty(_error.text) ? DisplayStyle.None : DisplayStyle.Flex;
            var ready = _workspace != null;
            var idle = ready && !FolderOperationRunning;
            _copyPage?.SynchronizeSettings();
            _analysisPage?.SynchronizeSettings();
            _fontPage?.SynchronizeSettings();
            _refreshRequested = false;
            RefreshSharedControls();
            _scanAll.SetEnabled(idle);
            _scanCurrent.SetEnabled(idle);
            _selection.SetEnabled(idle);
            _allAssets.SetEnabled(idle);
            _settingsMenu.SetEnabled(!ready || idle);
            _standards.SetEnabled(idle);
            _cancel.SetEnabled(ready && _workspace.Running);
            _cancel.style.display = ready && _workspace.Running ? DisplayStyle.Flex : DisplayStyle.None;
            _scanCurrent.text = "检查" + AssetCheckWorkspace.KindName(_kind);
            _typeTabs.Select(_kind);
            _notice.tooltip = AssetCheckCatalog.Entries.Single(value => value.Kind == _kind).Notice +
                "\n勾选按整个文件操作。修改选中遵循问题筛选；修改全部处理当前类型的全部已确认问题。";
            if (!ready)
            {
                _overview.Bind(null);
                _fixAll.SetEnabled(false);
                _fixSelected.SetEnabled(false);
                return;
            }

            _scope.SetValueWithoutNotify(AssetDatabase.LoadAssetAtPath<DefaultAsset>(_workspace.Settings.scope));
            _scope.tooltip = _workspace.Settings.scope;
            var report = _workspace.GetReport(_kind);
            _overview.Bind(report);
            _results.Bind(report, _workspace.Running);
            foreach (var registration in AssetCheckCatalog.Entries) RefreshTab(registration.Kind);
            RefreshSelection();

            // 忽略按整个文件跨类型生效，因此每个页签都提供恢复入口。
            var ignored = _workspace.Settings.ignoredAssets.ToList();
            _ignoredExclusions = new AssetCheckExclusions(_workspace.Settings);
            _ignoredFoldout.text = "已忽略资源（文件／文件夹，共 " + ignored.Count + " 条记录）";
            _ignoredFoldout.style.display = ignored.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _ignored.itemsSource = ignored;
            _ignored.style.height = Math.Min(6, ignored.Count) * 28 + 2;
            _status.text = _workspace.Failure == null ? _workspace.Activity : _workspace.Activity + " 错误：" + _workspace.Failure;
            if (!string.IsNullOrEmpty(_workspace.ReportSaveFailure))
                _status.text += " " + _workspace.ReportSaveFailure;
            if (!string.IsNullOrEmpty(_workspace.ReportLoadFailure)) _status.text += " " + _workspace.ReportLoadFailure;
            _status.tooltip = _status.text;
            _progress.value = _workspace.Progress;
            _progress.style.display = _workspace.Running ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshSelection()
        {
            var report = _workspace?.GetReport(_kind);
            var issues = _results.SelectedFixes;
            var all = _results.AllFixes;
            _fixAll.SetEnabled(CanModify(report) && all.Length > 0);
            _fixAll.text = DescribeFix("修改全部", all);
            _fixAll.tooltip = DescribeFixScope(all) + "\n修改当前类型全部已确认问题，不受问题筛选和折叠影响；未验证目标需明确设为标准。";
            _fixSelected.SetEnabled(CanModify(report) && issues.Length > 0);
            _fixSelected.text = DescribeFix("修改选中", issues);
            _fixSelected.tooltip = DescribeFixScope(issues) + "\n只修改勾选文件中符合当前筛选的已确认问题。";
        }

        private bool CanModify(AssetCheckReport report) => report != null && !report.IsSnapshot && report.Complete &&
            !report.Cancelled && !report.Stale && report.Failure == null && !_workspace.Running;

        private static string DescribeFix(string label, AssetCheckIssue[] issues) => label +
            $"（{issues.Select(issue => issue.TargetId).Distinct().Count()}）";

        private static string DescribeFixScope(AssetCheckIssue[] issues) =>
            $"{issues.Select(issue => issue.TargetId).Distinct().Count()} 个文件，{issues.Length} 项设置";

        private void RefreshTab(AssetCheckKind kind)
        {
            var report = _workspace.GetReport(kind);
            _typeTabs.Bind(kind, report?.ProblemAssets.ToString() ?? "—",
                report == null ? "尚未检查" :
                $"{report.ProblemAssets} 个问题文件，{report.UnverifiedOnlyFiles} 个仅未验证文件；Tab 数字仅表示问题文件数。" +
                (report.Stale ? "\n上次检查结果，请重新检查后修改。" : ""),
                report != null && report.Complete && report.ProblemAssets == 0);
        }
    }
}
