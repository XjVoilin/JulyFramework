using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.Physics.Authoring
{
    internal enum ColliderSort { Name, Count, Kinds, TotalChange, NativeChange, ConvexChange, Vertices, Triangles, ConvexCount, LargestHull, Performance }
    internal enum ColliderFilter { All, NativeOnly, MeshOnly, Mixed }
    internal enum ColliderData { Current, Candidate, Model }

    public class ColliderOverviewWindow : EditorWindow
    {
        static readonly string[] QueueNames = { "全部", "新纳入", "需复审", "异常", "待处理", "已完成" };
        static readonly string[] CompositionNames = { "全部", "仅原生碰撞体", "仅网格碰撞体", "混合组成" };
        static readonly string[] DisplayNames = { "弱化叠加", "仅碰撞体", "完整穿透线框" };
        readonly Dictionary<ColliderRow,ColliderPerformanceReport[]> performanceReports = new Dictionary<ColliderRow,ColliderPerformanceReport[]>();
        ColliderPerformanceFilter performanceFilter;
        readonly HashSet<string> excludedPerformanceColumns = new HashSet<string>();
        readonly Dictionary<string,Toggle> performanceSources = new Dictionary<string,Toggle>();
        readonly Dictionary<ColliderPerformanceFilter,Button> performanceTags = new Dictionary<ColliderPerformanceFilter,Button>();
        int[] selectedPerformanceColumns = Array.Empty<int>();
        bool performanceAllConvex;
        Toggle performanceConvex;
        Label performanceSummary;
        readonly HashSet<ColliderRowView> views = new HashSet<ColliderRowView>();
        readonly List<ColliderRow> visibleRows = new List<ColliderRow>();
        ColliderWorkspace workspace;
        ColliderPreview renderer;
        ListView list;
        VisualElement bulk;
        ToolbarMenu viewMenu, toolsMenu, modelMenu;
        readonly Dictionary<ColliderQueue, Button> queueTags = new Dictionary<ColliderQueue, Button>();
        readonly Dictionary<ColliderQueue, Button> detailTags = new Dictionary<ColliderQueue, Button>();
        ToolbarMenu[] columnMenus;
        Label empty;
        Label count, selection, message, source, writes, differences, geometryTotals;
        Button read, refresh, generate, apply, cancel;
        readonly List<VisualElement> filters = new List<VisualElement>();
        ColliderQueue queue = ColliderQueue.Pending, detailFilter = ColliderQueue.All;
        ColliderSort sort;
        Comparison<ColliderRow> projectSort;
        string projectSortLabel;
        ColliderData dataSource, sortSource;
        ColliderFilter composition;
        ColliderTargetSelection scope;
        int filterIndex;
        string sortTarget;
        string search = "", actionError;
        bool descending, projectChanged;
        int viewIndex;
        ColliderDisplay displayMode;

        public ColliderWorkspace Workspace => workspace;
        protected virtual IColliderAuthoringAdapter CreateAdapter() => new DefaultColliderAuthoringAdapter();
        internal ColliderTargetSelection[] ScopeOptions => new[] { ColliderTargetSelection.All }.Concat(workspace.Profile.Targets.Length > 1 ? workspace.Profile.Targets.Select(t => ColliderTargetSelection.Only(t.Id)) : Enumerable.Empty<ColliderTargetSelection>()).ToArray();
        internal string ScopeName(ColliderTargetSelection value) => workspace.ScopeName(value);
        internal string RowScopeName(ColliderTargetSelection value) => value.TargetId == null ?
            ScopeName(value) : workspace.Profile.Targets.Single(t => t.Id == value.TargetId).Name;
        internal ColliderTargetSelection Scope => scope;

        [MenuItem("JulyGF/资源管理/碰撞体总览", false, 66)]
        public static void Open()
        {
            bool opened = HasOpenInstances<ColliderOverviewWindow>();
            var window = GetWindow<ColliderOverviewWindow>("碰撞体总览");
            window.minSize = new Vector2(740, 430);
            if (!opened) window.position = new Rect(window.position.position, new Vector2(1100, 800));
            window.Show();
        }

        protected virtual void OnEnable()
        {
            workspace = new ColliderWorkspace(CreateAdapter());
            titleContent = new GUIContent(workspace.Profile.Title); filterIndex = workspace.Profile.Targets.Length - 1; sortTarget = workspace.Profile.Targets[0].Id;
            workspace.Changed += Refresh;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        protected virtual void BuildProjectTools(VisualElement header) { }
        protected virtual void BuildProjectSortMenu(DropdownMenu menu) { }
        protected bool IsProjectSort(Comparison<ColliderRow> comparison) => projectSort == comparison;
        protected void ToggleProjectSort(string label, Comparison<ColliderRow> ascendingOrder, Comparison<ColliderRow> descendingOrder)
        {
            bool reverse = projectSort == ascendingOrder;
            projectSort = (reverse ? descendingOrder : ascendingOrder) ??
                throw new ArgumentNullException(reverse ? nameof(descendingOrder) : nameof(ascendingOrder));
            projectSortLabel = label + (reverse ? "↓" : "↑");
            sort = ColliderSort.Name; descending = false; sortSource = ColliderData.Model; sortTarget = null;
            ApplySort(); Refresh();
            if (visibleRows.Count > 0) list.ScrollToItem(0);
        }
        public virtual void CreateGUI()
        {
            foreach (var view in views) view.Release(); views.Clear();
            rootVisualElement.Clear(); filters.Clear(); queueTags.Clear(); detailTags.Clear(); performanceSources.Clear(); performanceTags.Clear();
            scope = workspace.ValidScope(scope);
            if (!workspace.Profile.Targets.Any(t => t.Id == sortTarget)) sortTarget = workspace.Profile.Targets[0].Id;
            columnMenus = new ToolbarMenu[workspace.Profile.Targets.Length * 2];
            rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.physics.authoring/Editor/Workbench/ColliderOverviewWindow.uss"));
            rootVisualElement.AddToClassList("collider-colliders");
            rootVisualElement.EnableInClassList("collider-dark",EditorGUIUtility.isProSkin);
            rootVisualElement.EnableInClassList("collider-light",!EditorGUIUtility.isProSkin);
            var header = new VisualElement(); header.AddToClassList("collider-header"); rootVisualElement.Add(header);
            var toolbar = Line(); toolbar.AddToClassList("collider-resource-toolbar");
            read = new Button(ReadSelection) { text = "读取所选", tooltip = "读取Prefab或目录，自动按GUID登记新资源并恢复已有审阅记录；不生成或修改Prefab。" }; toolbar.Add(read);
            refresh = new Button(() => Run(workspace.RefreshSelection)) { text = "刷新现状", tooltip = "重新扫描上次读取的范围，发现新GUID并恢复审阅结论；不写Prefab。" }; toolbar.Add(refresh);
            source = new Label("Project：选择Prefab或文件夹") { name = "collider-source" }; source.style.flexGrow = 1; toolbar.Add(source);
            var find = new ToolbarSearchField { name = "collider-search", tooltip = "搜索物品名称或资源路径" }; find.SetValueWithoutNotify(search);
            find.RegisterValueChangedCallback(e => { search = e.newValue; Refresh(); }); filters.Add(find); toolbar.Add(find);
            count = new Label(); count.AddToClassList("collider-secondary");
            header.Add(toolbar);
            BuildProjectTools(header);
            var tags = Line(); tags.name = "collider-status-tabs"; tags.AddToClassList("collider-tagbar"); header.Add(tags);
            var statusLabel = new Label("状态"); statusLabel.AddToClassList("collider-tag-label"); tags.Add(statusLabel);
            foreach (var value in new[] { ColliderQueue.All, ColliderQueue.Pending, ColliderQueue.Done })
            {
                var tag = new Button(() => { queue = value; Refresh(); }) { name = "collider-tag-" + value };
                tag.AddToClassList("collider-tag"); tags.Add(tag); filters.Add(tag); queueTags.Add(value, tag);
            }
            var details = Line(); details.name = "collider-detail-filters"; details.AddToClassList("collider-tagbar"); header.Add(details);
            var filterLabel = new Label("筛选"); filterLabel.AddToClassList("collider-tag-label"); details.Add(filterLabel);
            foreach (var value in new[] { ColliderQueue.All, ColliderQueue.Issues, ColliderQueue.New, ColliderQueue.Recheck })
            {
                var tag = new Button(() => { detailFilter = value; Refresh(); }) { name = "collider-filter-" + value };
                tag.AddToClassList("collider-tag"); details.Add(tag); filters.Add(tag); detailTags.Add(value, tag);
            }
            var clear = new Button(() => { queue = ColliderQueue.All; detailFilter = ColliderQueue.All; composition = ColliderFilter.All; performanceFilter = ColliderPerformanceFilter.All; performanceAllConvex = false; excludedPerformanceColumns.Clear(); search = ""; find.SetValueWithoutNotify(search); Refresh(); })
                { name = "collider-clear-filters", text = "清除筛选", tooltip = "恢复全部状态，清除搜索、组成和性能筛选。" };
            clear.AddToClassList("collider-clear-filters"); tags.Add(clear); filters.Add(clear);
            BuildPerformanceControls(header);
            viewMenu = new ToolbarMenu { name = "collider-view-menu", text = "视图" }; toolbar.Add(viewMenu); filters.Add(viewMenu);
            for (int i = 0; i < DisplayNames.Length; i++)
            {
                var value = (ColliderDisplay)i;
                viewMenu.menu.AppendAction("显示/" + DisplayNames[i], _ => { displayMode = value; InvalidateViews(); Refresh(); },
                    _ => displayMode == value ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            var poses = new[] { new Vector2(-15, 145), Vector2.zero, new Vector2(0, 180), new Vector2(0, 90) };
            var poseNames = new[] { "斜视", "正面", "背面", "侧面" };
            for (int i = 0; i < poses.Length; i++)
            {
                int value = i;
                viewMenu.menu.AppendAction("视角/" + poseNames[i], _ => { viewIndex = value; foreach (var row in workspace.Rows) row.Rotation = poses[value]; InvalidateViews(); },
                    _ => viewIndex == value ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            toolsMenu = new ToolbarMenu { name = "collider-tools", text = "更多" }; toolbar.Add(toolsMenu); filters.Add(toolsMenu);
            toolsMenu.menu.AppendAction("全选可见", _ => SelectAll(true));
            toolsMenu.menu.AppendAction("取消可见勾选", _ => SelectAll(false));
            toolsMenu.menu.AppendAction("重新分析并生成所选", _ => Generate(SelectedRows(), Scope, true),
                _ => !workspace.Busy && SelectedRows().Length > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            toolsMenu.menu.AppendSeparator();
            toolsMenu.menu.AppendAction("制作设置", _ => ColliderAuthoringSettingsWindow.Open(workspace, () => { filterIndex = Math.Min(filterIndex, workspace.Profile.Targets.Length - 1); sort = ColliderSort.Name; CreateGUI(); }));
            toolsMenu.menu.AppendAction("操作说明", _ => EditorUtility.DisplayDialog("碰撞体总览",
                "读取后比较当前和候选；生成不会修改Prefab，应用直接替换所选组，不备份。\n拖动预览同步旋转，不缩放。点击名称只定位Prefab。\n整体包围栏“复用当前”原样暂存当前形状；结构贴合栏“复用整体候选”直接复制整体候选，不执行生成验收。\n候选栏调参只影响当前Prefab和本栏；生成和应用仅处理当前项或可见选中项，并使用各自参数。\n应用后检查并点击“标记完成”；可在已完成Tag点击“标为待处理”重新审阅，碰撞体和候选保持。\n列头菜单按该列排序或组成筛选；当前按Collider类型，候选按实际碰撞体类型统计。当前Mesh预览为源网格。", "关闭"));
            var columns = new VisualElement { name = "collider-columns" }; columns.AddToClassList("collider-five"); header.Add(columns);
            var reference = new VisualElement(); reference.AddToClassList("collider-reference"); columns.Add(reference);
            modelMenu = new ToolbarMenu { name = "collider-model-column", text = "原模型", tooltip = "按原模型属性排序，再次选择内置排序项切换升降序。" };
            modelMenu.AddToClassList("collider-column-title"); reference.Add(modelMenu); filters.Add(modelMenu);
            modelMenu.menu.AppendAction("名称", _ =>
            {
                sortSource = ColliderData.Model; sortTarget = null; SortBy(ColliderSort.Name);
            }, _ => workspace.Busy ? DropdownMenuAction.Status.Disabled :
                projectSort == null && sort == ColliderSort.Name ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            foreach (var value in new[] { ColliderSort.Vertices, ColliderSort.Triangles })
            {
                var column = value;
                modelMenu.menu.AppendAction(column == ColliderSort.Vertices ? "顶点数" : "三角面数", _ =>
                {
                    if (sortSource != ColliderData.Model) sort = ColliderSort.Name;
                    sortSource = ColliderData.Model; sortTarget = null; SortBy(column);
                }, _ => workspace.Busy ? DropdownMenuAction.Status.Disabled : sortSource == ColliderData.Model && sort == column ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            BuildProjectSortMenu(modelMenu.menu);
            var selectionActions = new VisualElement(); selectionActions.AddToClassList("collider-select-actions"); reference.Add(selectionActions);
            selectionActions.Add(new Button(() => SelectAll(true)) { text = "全选可见", name = "collider-select-all", tooltip = "勾选当前筛选结果中的全部物品。" });
            selectionActions.Add(new Button(() => SelectAll(false)) { text = "全不选", name = "collider-select-none", tooltip = "取消当前筛选结果中的全部勾选，隐藏项的勾选保持。" });
            for (int goal = 0; goal < workspace.Profile.Targets.Length; goal++)
            {
                var group = new VisualElement(); group.AddToClassList("collider-goal"); columns.Add(group);
                var target = workspace.Profile.Targets[goal];
                if (target.FixedGoal)
                {
                    var title = new Label(target.Name); title.AddToClassList("collider-column-title"); title.tooltip = target.Path; group.Add(title);
                }
                else
                {
                    var mode = new PopupField<string>(target.Name, new List<string> { "整体包围", "结构贴合" }, (int)target.Settings.Goal) { name = "collider-goal-" + target.Id };
                    mode.AddToClassList("collider-column-title"); mode.RegisterValueChangedCallback(e => Run(() => workspace.ChangeGoal(target.Id, (ColliderGeometryGoal)mode.index)));
                    group.Add(mode); filters.Add(mode);
                }
                var pair = new VisualElement(); pair.AddToClassList("collider-pair"); group.Add(pair);
                for (int sourceIndex = 0; sourceIndex < 2; sourceIndex++)
                {
                    int index = goal * 2 + sourceIndex;
                    var menu = new ToolbarMenu { name = "collider-column-" + index };
                    menu.AddToClassList("collider-preview"); menu.AddToClassList(sourceIndex == 0 ? "collider-current-heading" : "collider-candidate-heading");
                    columnMenus[index] = menu; pair.Add(menu); filters.Add(menu);
                    BindColumnMenu(menu, goal, (ColliderData)sourceIndex);
                }
            }
            list = new ListView { itemsSource = visibleRows, fixedItemHeight = 420,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight, selectionType = SelectionType.None };
            list.style.flexGrow = 1; list.style.minHeight = 0;
            var scroll = list.Q<ScrollView>();
            scroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.width > 0) columns.style.width = e.newRect.width;
            });
            list.makeItem = () => { var row = new ColliderRowView(this); views.Add(row); return row.Root; };
            list.bindItem = (element, i) => ((ColliderRowView)element.userData).Bind(visibleRows[i]);
            list.unbindItem = (element, i) => ((ColliderRowView)element.userData).Release();
            list.destroyItem = element => { var row = (ColliderRowView)element.userData; row.Release(); views.Remove(row); };
            rootVisualElement.Add(list);
            empty = new Label { name = "collider-empty" }; empty.AddToClassList("collider-empty"); rootVisualElement.Add(empty);
            var batch = new VisualElement(); batch.AddToClassList("collider-batch"); rootVisualElement.Add(batch);
            var actions = Line(); actions.name = "collider-selection-actions"; actions.AddToClassList("collider-batch-actions"); batch.Add(actions);
            actions.Add(count);
            selection = new Label { name = "collider-selection-count", tooltip = "仅统计当前可见项的勾选；隐藏项不参与生成和应用。" }; actions.Add(selection);
            Popup(actions, "范围", ScopeOptions.Select(RowScopeName).ToArray(), Array.IndexOf(ScopeOptions, scope), i => scope = ScopeOptions[i], "collider-scope");
            actions.Q<PopupField<string>>("collider-scope").style.display = workspace.Profile.Targets.Length > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            actions.Add(new VisualElement { style = { flexGrow = 1 } });
            generate = new Button(() => Generate(SelectedRows(), scope)) { name = "collider-generate-selected", text = "生成选中预览", tooltip = "只重新计算当前可见选中项的预览，不写Prefab。" }; actions.Add(generate);
            apply = new Button(() => Run(() => workspace.Apply(SelectedRows(), scope))) { name = "collider-apply-selected", text = "应用选中预览", tooltip = "只应用当前可见选中项在所选范围内已有的候选组。某目标组过期或配置未就绪时，该Prefab不写入。" }; actions.Add(apply);
            apply.AddToClassList("collider-apply");
            bulk = Line(); bulk.name = "collider-bulk"; bulk.AddToClassList("collider-batch-summary"); batch.Add(bulk);
            writes = new Label { name = "collider-write-summary" }; writes.AddToClassList("collider-secondary"); bulk.Add(writes);
            differences = new Label { name = "collider-differences" }; bulk.Add(differences);
            differences.RegisterCallback<ClickEvent>(e =>
            {
                if (e.button == 0) EditorUtility.DisplayDialog("预计碰撞体数量变化", differences.tooltip, "关闭");
            });
            geometryTotals = new Label { name = "collider-geometry-totals", tooltip = ColliderStatistics.GeometryNote };
            geometryTotals.AddToClassList("collider-geometry-summary"); batch.Add(geometryTotals);
            var status = Line(); status.AddToClassList("collider-status"); batch.Add(status);
            message = new Label { name = "collider-message" }; status.Add(message);
            cancel = new Button(workspace.Cancel) { name = "collider-cancel", text = "取消生成" }; status.Add(cancel);
            Refresh();
        }

        internal static VisualElement Line()
        {
            var line = new VisualElement(); line.AddToClassList("collider-line"); return line;
        }

        string PerformanceKey(int column) => workspace.Profile.Targets[column/2].Id + ":" + column%2;

        void BuildPerformanceControls(VisualElement header)
        {
            var sources = Line(); sources.name = "collider-performance-controls";
            sources.AddToClassList("collider-performance-controls"); header.Add(sources);
            var title = new Label("检查范围"); title.AddToClassList("collider-performance-label"); sources.Add(title);
            for (int target = 0; target < workspace.Profile.Targets.Length; target++)
            {
                var group = new VisualElement(); group.AddToClassList("collider-performance-group"); sources.Add(group);
                group.Add(new Label(workspace.Profile.Targets[target].Name));
                for (int data = 0; data < 2; data++)
                {
                    int column = target*2+data; string key = PerformanceKey(column);
                    var toggle = new Toggle(data == 0 ? "当前" : "候选") {
                        name = "collider-performance-source-" + workspace.Profile.Targets[target].Id + "-" + data,
                        tooltip = workspace.Profile.Targets[target].Name + (data == 0 ? "：检查Prefab现有碰撞体。" : "：检查暂存候选。") +
                            "可同时勾选多列，任一勾选列符合显示条件即显示该物品。" };
                    toggle.SetValueWithoutNotify(!excludedPerformanceColumns.Contains(key));
                    toggle.RegisterValueChangedCallback(change => {
                        if (change.newValue) excludedPerformanceColumns.Remove(key); else excludedPerformanceColumns.Add(key);
                        Refresh();
                    });
                    group.Add(toggle); filters.Add(toggle); performanceSources.Add(key,toggle);
                }
            }
            var all = new Button(() => { excludedPerformanceColumns.Clear(); Refresh(); }) {
                name = "collider-performance-all-sources", text = "全部列", tooltip = "同时检查所有当前和候选列。" };
            sources.Add(all); filters.Add(all);
            var levels = Line(); levels.AddToClassList("collider-performance-controls"); header.Add(levels);
            var label = new Label("提醒筛选"); label.AddToClassList("collider-performance-label"); levels.Add(label);
            var values = new[] { ColliderPerformanceFilter.All, ColliderPerformanceFilter.Reminder,
                ColliderPerformanceFilter.Attention, ColliderPerformanceFilter.RuleMismatch };
            var names = new[] { "全部物品", "有提醒", "重点及以上", "规则不符" };
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                var button = new Button(() => { performanceFilter = value; Refresh(); }) {
                    name = "collider-performance-filter-" + value, text = names[i] };
                button.AddToClassList("collider-performance-filter-tag"); levels.Add(button); filters.Add(button); performanceTags.Add(value,button);
            }
            performanceConvex = new Toggle("仅全凸包") { name = "collider-performance-convex",
                tooltip = "同一勾选列必须同时满足提醒条件和全凸包条件；全凸包本身不代表性能差。" };
            performanceConvex.RegisterValueChangedCallback(change => { performanceAllConvex = change.newValue; Refresh(); });
            levels.Add(performanceConvex); filters.Add(performanceConvex);
            performanceSummary = new Label { name = "collider-performance-summary",
                tooltip = "按状态、搜索和组成筛选后的范围统计，尚未按提醒等级筛选。每件物品只按勾选列中的最高等级计一次；全凸包可重叠。只提示和筛选，不限制复用或应用。" };
            performanceSummary.AddToClassList("collider-performance-summary"); header.Add(performanceSummary);
        }

        bool MatchesPerformance(ColliderRow row)
        {
            var reports = performanceReports[row];
            foreach (int column in selectedPerformanceColumns)
                if (reports[column].Matches(performanceFilter) && (!performanceAllConvex || reports[column].AllConvex)) return true;
            return false;
        }

        void RefreshPerformanceControls()
        {
            foreach (var entry in performanceSources) entry.Value.SetValueWithoutNotify(!excludedPerformanceColumns.Contains(entry.Key));
            foreach (var entry in performanceTags) entry.Value.EnableInClassList("collider-tag-active",entry.Key == performanceFilter);
            performanceConvex.SetValueWithoutNotify(performanceAllConvex);
            if (selectedPerformanceColumns.Length == 0)
            { performanceSummary.text = "未选择检查列，请勾选当前／候选或点击“全部列”。"; return; }
            int count = 0, reminder = 0, attention = 0, mismatch = 0, convex = 0;
            foreach (var row in workspace.Rows)
            {
                if (!MatchesContext(row)) continue;
                var reports = performanceReports[row]; var level = ColliderPerformanceLevel.None; bool allConvex = false;
                foreach (int column in selectedPerformanceColumns)
                {
                    var report = reports[column]; allConvex |= report.AllConvex;
                    if ((!performanceAllConvex || report.AllConvex) && report.Level > level) level = report.Level;
                }
                if (performanceAllConvex && !allConvex) continue;
                count++; if (allConvex) convex++;
                if (level == ColliderPerformanceLevel.Reminder) reminder++;
                else if (level == ColliderPerformanceLevel.Attention) attention++;
                else if (level == ColliderPerformanceLevel.RuleMismatch) mismatch++;
            }
            performanceSummary.text = $"范围 {count} 件 · 提醒 {reminder} · 重点 {attention} · 规则不符 {mismatch} · 全凸包 {convex}";
        }

        void Popup(VisualElement parent, string label, string[] choices, int index, Action<int> change, string name)
        {
            var field = new PopupField<string>(label, choices.ToList(), index) { name = name };
            field.RegisterValueChangedCallback(e => { change(field.index); Refresh(); });
            filters.Add(field); parent.Add(field);
        }

        internal void Run(Action action)
        {
            try { actionError = null; action(); }
            catch (Exception error) { actionError = error.Message; Debug.LogException(error); }
            Refresh();
        }

        void ReadSelection() => Run(() =>
        {
            workspace.Read(Selection.objects); ApplySort();
            source.text = string.Join("、", Selection.objects.Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).Select(System.IO.Path.GetFileName));
            source.tooltip = source.text + "\n" + string.Join("\n", workspace.SkippedReasons);
        });

        internal async void Generate(ColliderRow[] rows, ColliderTargetSelection selectedScope, bool rebuildPickingAnalysis = false)
        {
            try { actionError = null; await workspace.GenerateAsync(rows, selectedScope, rebuildPickingAnalysis); }
            catch (Exception error) { actionError = error.Message; Debug.LogException(error); }
            if (this != null) Refresh();
        }

        void SelectAll(bool value) { foreach (var row in visibleRows) row.Selected = value; Refresh(); }
        internal ColliderRow[] SelectedRows() => visibleRows.Where(r => r.Selected).ToArray();

        void BindColumnMenu(ToolbarMenu menu, int goal, ColliderData source)
        {
            void Sort(ColliderSort value)
            {
                if (sortSource != source || sortTarget != workspace.Profile.Targets[goal].Id) sort = ColliderSort.Name;
                sortSource = source; sortTarget = workspace.Profile.Targets[goal].Id;
                SortBy(value);
            }
            void AddSort(string label, ColliderSort value) => menu.menu.AppendAction(label, _ => Sort(value),
                _ => workspace.Busy ? DropdownMenuAction.Status.Disabled : sortSource == source && sortTarget == workspace.Profile.Targets[goal].Id && sort == value ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            AddSort("碰撞体数", ColliderSort.Count); AddSort("碰撞体类型数", ColliderSort.Kinds);
            AddSort("碰撞网格顶点数", ColliderSort.Vertices); AddSort("碰撞网格三角面数", ColliderSort.Triangles);
            AddSort("凸包数量",ColliderSort.ConvexCount);AddSort("单凸包最大顶点数",ColliderSort.LargestHull);AddSort("性能等级",ColliderSort.Performance);
            if (source == ColliderData.Candidate)
            {
                menu.menu.AppendSeparator(); AddSort("碰撞体总数变化", ColliderSort.TotalChange); AddSort("原生碰撞体数变化", ColliderSort.NativeChange); AddSort("凸包数变化", ColliderSort.ConvexChange);
            }
            menu.menu.AppendSeparator();
            for (int i = 0; i < CompositionNames.Length; i++)
            {
                var value = (ColliderFilter)i;
                menu.menu.AppendAction("组成/" + CompositionNames[i], _ => { filterIndex = goal; dataSource = source; composition = value; Refresh(); },
                    _ => composition == value && (value == ColliderFilter.All || filterIndex == goal && dataSource == source)
                        ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
        }

        (int count, int kinds, int native, int mesh, bool exists) Metrics(ColliderRow row, int index, ColliderData source)
        {
            if (source == ColliderData.Current)
            { var value = row.Inspection.Current[index]; return (value.Count, value.KindCount, value.NativeCount, value.MeshCount, value.Count > 0); }
            var preview = row.Candidates[index].Preview;
            return preview == null ? (0, 0, 0, 0, false) : (preview.Count, preview.KindCount, preview.NativeCount, preview.Count - preview.NativeCount, true);
        }

        bool Matches(ColliderRow row) => MatchesContext(row) && MatchesPerformance(row);

        bool MatchesContext(ColliderRow row)
        {
            if (!row.InQueue(queue) || (queue != ColliderQueue.Done && !row.InQueue(detailFilter)) || (!row.Name.Contains(search, StringComparison.OrdinalIgnoreCase) && !row.Path.Contains(search, StringComparison.OrdinalIgnoreCase))) return false;
            var value = Metrics(row, filterIndex, dataSource);
            return composition == ColliderFilter.All || value.exists && (composition == ColliderFilter.NativeOnly ? value.native == value.count :
                composition == ColliderFilter.MeshOnly ? value.mesh == value.count : value.native > 0 && value.mesh > 0);
        }

        internal void SortBy(ColliderSort column)
        {
            bool wasProjectSort = projectSort != null;
            projectSort = null; projectSortLabel = null;
            if (IsChange(column)) sortSource = ColliderData.Candidate;
            descending = !wasProjectSort && sort == column ? !descending : column != ColliderSort.Name;
            sort = column; ApplySort(); Refresh();
            if (visibleRows.Count > 0) list.ScrollToItem(0);
        }

        static bool IsChange(ColliderSort value) => value is ColliderSort.TotalChange or ColliderSort.NativeChange or ColliderSort.ConvexChange;
        static string SortLabel(ColliderSort value) => value switch
        {
            ColliderSort.Count => "碰撞体数", ColliderSort.Kinds => "类型数", ColliderSort.Vertices => "顶点", ColliderSort.Triangles => "三角面", ColliderSort.TotalChange => "Δ总",
            ColliderSort.NativeChange => "Δ原生", ColliderSort.ConvexChange => "Δ凸", ColliderSort.ConvexCount => "凸包",ColliderSort.LargestHull => "单凸包顶点",ColliderSort.Performance => "性能", _ => "名称"
        };

        internal ColliderPerformanceReport Performance(ColliderRow row,int index,ColliderData source) => performanceReports[row][index*2+(int)source];
        void RefreshPerformance()
        {
            selectedPerformanceColumns = Enumerable.Range(0, workspace.Profile.Targets.Length*2)
                .Where(column => !excludedPerformanceColumns.Contains(PerformanceKey(column))).ToArray();
            performanceReports.Clear();
            foreach(var row in workspace.Rows)
            {
                var reports=new ColliderPerformanceReport[workspace.Profile.Targets.Length*2];
                for(int i=0;i<workspace.Profile.Targets.Length;i++) {reports[i*2]=ColliderPerformance.Current(row,i);reports[i*2+1]=ColliderPerformance.Candidate(row.Candidates[i].Preview,workspace.Profile.Performance);}
                performanceReports.Add(row,reports);
            }
        }

        (long value, bool exists) SortValue(ColliderRow row)
        {
            if (sortSource == ColliderData.Model)
            {
                var model = row.Inspection.ModelGeometry;
                return (sort == ColliderSort.Vertices ? model.Vertices : model.Triangles, row.Inspection.Ready && model.MissingMeshes == 0);
            }
            int index = Array.FindIndex(workspace.Profile.Targets, t => t.Id == sortTarget);
            if(sort==ColliderSort.Performance||sort==ColliderSort.LargestHull||sort==ColliderSort.ConvexCount)
            {
                var report=Performance(row,index,sortSource);
                return(sort==ColliderSort.Performance?(long)report.Level:sort==ColliderSort.LargestHull?report.MaxHullVertices:
                    sortSource==ColliderData.Current?ColliderStatistics.Current(row,index).Convex:row.Candidates[index].Preview==null?0:ColliderStatistics.Candidate(row.Candidates[index].Preview).Convex,report.Exists);
            }
            if (sort == ColliderSort.Vertices || sort == ColliderSort.Triangles)
            {
                var preview = row.Candidates[index].Preview;
                if (sortSource == ColliderData.Candidate && preview == null) return (0, false);
                var geometry = sortSource == ColliderData.Current ? row.Inspection.Current[index].Geometry : ColliderGeometryCounts.Candidate(preview);
                bool exists = sortSource == ColliderData.Candidate || row.Inspection.Ready && row.Inspection.Current[index].Count > 0 && geometry.MissingMeshes == 0;
                return (sort == ColliderSort.Vertices ? geometry.Vertices : geometry.Triangles, exists);
            }
            if (!IsChange(sort))
            {
                var metrics = Metrics(row, index, sortSource);
                return (sort == ColliderSort.Kinds ? metrics.kinds : metrics.count, metrics.exists);
            }
            if (!row.IsCurrent(index)) return (0, false);
            var before = ColliderStatistics.Current(row, index);
            var after = ColliderStatistics.Candidate(row.Candidates[index].Preview);
            return (sort switch
            {
                ColliderSort.NativeChange => after.Native - before.Native,
                ColliderSort.ConvexChange => after.Convex - before.Convex,
                _ => after.Total - before.Total
            }, true);
        }

        void ApplySort() => workspace.Rows.Sort((a, b) =>
        {
            if (projectSort != null)
            {
                int value = projectSort(a, b);
                if (value != 0) return value;
            }
            else if (sort != ColliderSort.Name)
            {
                var x = SortValue(a); var y = SortValue(b);
                if (x.exists != y.exists) return x.exists ? -1 : 1;
                int value = x.value.CompareTo(y.value);
                if (value != 0) return descending ? -value : value;
            }
            int order = EditorUtility.NaturalCompare(a.Name, b.Name);
            if (order == 0) order = EditorUtility.NaturalCompare(a.Path, b.Path);
            return sort == ColliderSort.Name && descending ? -order : order;
        });

        internal void Refresh()
        {
            if (list == null) return;
            RefreshPerformance();
            if (!workspace.Busy)
            {
                if (projectSort != null || sort != ColliderSort.Name || descending) ApplySort();
                var rows = workspace.Rows.Where(Matches).ToArray();
                if (!visibleRows.SequenceEqual(rows)) { visibleRows.Clear(); visibleRows.AddRange(rows); list.Rebuild(); }
            }
            rootVisualElement.Q("collider-detail-filters").style.display = queue == ColliderQueue.Done ? DisplayStyle.None : DisplayStyle.Flex;
            count.text = $"显示 {visibleRows.Count}/{workspace.Rows.Count}";
            bool hasRows = visibleRows.Count > 0;
            list.style.display = hasRows ? DisplayStyle.Flex : DisplayStyle.None;
            rootVisualElement.Q("collider-columns").style.display = hasRows ? DisplayStyle.Flex : DisplayStyle.None;
            empty.style.display = hasRows ? DisplayStyle.None : DisplayStyle.Flex;
            empty.text = workspace.Rows.Count > 0 && selectedPerformanceColumns.Length == 0 ? "请选择至少一个检查列\n可同时勾选当前和候选。" : workspace.Rows.Count == 0 ? "尚未读取物品\n在 Project 选择 Prefab 或文件夹，然后点击“读取所选”。" : "当前条件下没有物品\n调整状态、搜索或筛选条件。";
            string compositionFilter = composition == ColliderFilter.All ? "" : $" · {workspace.Profile.Targets[filterIndex].Name}{(dataSource == ColliderData.Current ? "当前" : "候选")}：{CompositionNames[(int)composition]}";
            count.tooltip = count.text + " · " + QueueNames[(int)queue] + (queue == ColliderQueue.Done || detailFilter == ColliderQueue.All ? "" : " · " + QueueNames[(int)detailFilter]) + compositionFilter;
            foreach (var entry in queueTags)
            {
                entry.Value.text = QueueNames[(int)entry.Key] + " " + workspace.Rows.Count(r => r.InQueue(entry.Key));
                entry.Value.EnableInClassList("collider-tag-active", queue == entry.Key);
                entry.Value.tooltip = "当前读取范围的处理状态：全部 = 待处理 + 已完成；已完成不使用下方筛选。";
            }
            var statusRows = workspace.Rows.Where(r => r.InQueue(queue)).ToArray();
            RefreshPerformanceControls();
            foreach (var entry in detailTags)
            {
                entry.Value.text = (entry.Key == ColliderQueue.All ? "不限" : QueueNames[(int)entry.Key]) + " " + statusRows.Count(r => r.InQueue(entry.Key));
                entry.Value.EnableInClassList("collider-tag-active", detailFilter == entry.Key);
                entry.Value.tooltip = "仅统计所选状态内的数量；各筛选可能重叠，搜索和组成筛选另行叠加。";
            }
            for (int i = 0; i < columnMenus.Length; i++)
            {
                int goal = i / 2; var source = (ColliderData)(i % 2);
                bool sorted = sort != ColliderSort.Name && sortSource == source && sortTarget == workspace.Profile.Targets[goal].Id;
                string order = sorted ? " " + SortLabel(sort) + (descending ? "↓" : "↑") : "";
                columnMenus[i].text = (source == ColliderData.Current ? "当前" : "候选") + order + (composition != ColliderFilter.All && filterIndex == goal && dataSource == source ? " ●" : "");
                columnMenus[i].tooltip = "按本列碰撞体数、碰撞网格量、凸包数或性能等级排序；再次选择切换升降序。" + (source == ColliderData.Current ? "当前按Collider类型统计。" : "候选按碰撞体类型统计。") + compositionFilter;
            }
            modelMenu.text = "原模型" + (projectSort != null ? " " + projectSortLabel : sortSource == ColliderData.Model ? " " + SortLabel(sort) + (descending ? "↓" : "↑") : "");
            int selected = visibleRows.Count(r => r.Selected);
            selection.text = $"已选 {selected}";
            geometryTotals.style.display = selected > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            geometryTotals.text = selected == 0 ? "" : ColliderStatistics.SelectedGeometry(SelectedRows(), workspace.Indices(scope).ToArray());
            rootVisualElement.Q<PopupField<string>>("collider-scope").tooltip = "选中项的处理范围：" + ScopeName(scope) + "；选择后不执行。";
            bulk.style.display = selected > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            read.SetEnabled(!workspace.Busy); refresh.SetEnabled(!workspace.Busy);
            foreach (var field in filters) field.SetEnabled(!workspace.Busy);
            bool editable = !workspace.Busy && !EditorApplication.isPlayingOrWillChangePlaymode;
            generate.SetEnabled(editable && selected > 0);
            var targets = SelectedRows().Select(r => (row: r, indices: r.CandidateIndices(scope))).Where(p => p.indices.Length > 0).ToArray();
            var ready = targets.Where(p => p.indices.All(p.row.CanApply)).ToArray();
            apply.SetEnabled(editable && ready.Length > 0);
            writes.text = $"可应用 {ready.Length}项/{ready.Sum(p => p.indices.Length)}组" + (targets.Length > ready.Length ? $" · 未就绪 {targets.Length - ready.Length} 项" : "");
            int missing = SelectedRows().Sum(r => workspace.Indices(scope).Count() - r.CandidateIndices(scope).Length);
            if (missing > 0) writes.text += $" · 无预览 {missing}组";
            writes.tooltip = writes.text;
            var before = new ColliderCounts[workspace.Profile.Targets.Length]; var after = new ColliderCounts[workspace.Profile.Targets.Length];
            var groups = new int[workspace.Profile.Targets.Length];
            foreach (var target in ready)
                foreach (int index in target.indices)
                {
                    before[index] += ColliderStatistics.Current(target.row, index);
                    after[index] += ColliderStatistics.Candidate(target.row.Candidates[index].Preview);
                    groups[index]++;
                }
            differences.style.display = ready.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            differences.text = ready.Length == 0 ? "" : ColliderStatistics.Change(before.Aggregate(default(ColliderCounts), (a, b) => a + b), after.Aggregate(default(ColliderCounts), (a, b) => a + b));
            differences.tooltip = writes.text + "\n" +
                string.Join("\n", Enumerable.Range(0, workspace.Profile.Targets.Length).Where(i => groups[i] > 0).Select(i =>
                    (workspace.Profile.Targets[i].Name + "：") + ColliderStatistics.Change(before[i], after[i]) + "\n" + before[i].Note));
            cancel.style.display = workspace.Busy ? DisplayStyle.Flex : DisplayStyle.None;
            message.text = actionError ?? workspace.Message;
            message.tooltip = message.text;
            message.parent.style.display = workspace.Busy || !string.IsNullOrEmpty(message.text) ? DisplayStyle.Flex : DisplayStyle.None;
            message.EnableInClassList("collider-error", actionError != null);
            foreach (var view in views) view.Refresh();
        }

        internal bool IsVisible(VisualElement element) => element.worldBound.Overlaps(list.worldBound);
        internal Texture2D Render(ColliderPreview.Model model, ColliderRow row, ColliderPreviewShapes mesh, int column, int width, Bounds bounds)
        {
            if (renderer == null) renderer = new ColliderPreview();
            return renderer.Render(model, row, mesh, column, displayMode, width, 125, bounds);
        }
        void InvalidateViews() { foreach (var view in views) view.Dirty = true; }
        void Tick()
        {
            if (projectChanged && !workspace.Busy)
            { projectChanged = false; Run(workspace.RefreshProjectChanges); foreach (var view in views) view.ReloadModel(); }
            foreach (var view in views) { view.UpdateProgress(); view.RenderIfNeeded(); }
        }
        void OnPlayModeChanged(PlayModeStateChange state) => Refresh();
        void OnProjectChange() => projectChanged = true;
        protected virtual void OnDisable()
        {
            EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            list = null; performanceReports.Clear(); workspace.Changed -= Refresh; workspace.Dispose();
            foreach (var view in views) view.Release(); views.Clear(); visibleRows.Clear();
            renderer?.Dispose(); renderer = null;
        }
    }
}
