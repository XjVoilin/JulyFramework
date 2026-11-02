using System;
using System.Collections.Generic;
using System.Linq;
using July.Release.Editor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal sealed class BundleComparisonView : VisualElement
    {
        readonly BundleAnalysisCache _cache;
        readonly PopupField<BundleAnalysisRecord> _before, _after;
        readonly Button _compare, _packages, _resources;
        readonly TextField _search;
        readonly Toggle _changed;
        readonly Label _summary, _notice;
        readonly MultiColumnListView _list;
        readonly ScrollView _details;
        List<BundleComparisonRow> _rows = new List<BundleComparisonRow>();
        bool _resourceMode, _busy;
        internal BundleAnalysisComparison Comparison { get; private set; }

        internal BundleComparisonView(BundleAnalysisCache cache)
        {
            _cache = cache; name = "bundle-comparison"; style.flexGrow = 1; style.minHeight = 0;
            var choices = Toolbar();
            _before = Picker("基线", "comparison-before"); _after = Picker("目标", "comparison-after");
            choices.Add(_before); choices.Add(_after);
            _compare = new Button(Compare) { name = "compare-reports", text = "比较结果" }; choices.Add(_compare); Add(choices);
            _before.RegisterValueChangedCallback(_ => SelectionChanged()); _after.RegisterValueChangedCallback(_ => SelectionChanged());
            _summary = Text("comparison-summary"); Add(_summary);
            _notice = Text("comparison-notice"); Add(_notice);
            var tools = Toolbar();
            _packages = new Button(() => ShowResources(false)) { name = "comparison-packages", text = "AB 变化" };
            _resources = new Button(() => ShowResources(true)) { name = "comparison-resources", text = "资源变化" };
            tools.Add(_packages); tools.Add(_resources);
            _search = new TextField("搜索") { name = "comparison-search", style = { flexGrow = 1, flexShrink = 1, minWidth = 0 } }; tools.Add(_search);
            _changed = new Toggle("只看变化") { name = "comparison-changed", value = true }; tools.Add(_changed); Add(tools);
            _search.RegisterValueChangedCallback(_ => Rebuild()); _changed.RegisterValueChangedCallback(_ => Rebuild());
            _list = new MultiColumnListView { name = "comparison-results", fixedItemHeight = 42, selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                style = { flexGrow = 1, minWidth = 490, minHeight = 80 } };
            Column("name", "包／资源", 230, row => row.Name);
            Column("change", "变化", 155, row => row.Changed ? row.Change : "无变化");
            Column("before", "基线大小", 110, row => SizeCaption(row.BeforeExists, row.BeforeBytes, row.BeforeCoverage));
            Column("after", "目标大小", 110, row => SizeCaption(row.AfterExists, row.AfterBytes, row.AfterCoverage));
            Column("delta", "大小变化", 110, row => row.Delta.HasValue ? Signed(row.Delta.Value) : "不可比");
            _details = new ScrollView { name = "comparison-details", horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                style = { minWidth = 280, flexGrow = 1, paddingLeft = 10, paddingRight = 5 } };
            _list.selectionChanged += selected =>
            {
                _details.Clear(); var row = selected.OfType<BundleComparisonRow>().FirstOrDefault();
                if (row != null) foreach (var line in Comparison.Details(row)) _details.Add(new Label(line)
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 6, flexShrink = 0 } });
            };
            var split = new TwoPaneSplitView(1, 305, TwoPaneSplitViewOrientation.Horizontal) { name = "comparison-split", style = { flexGrow = 1, minHeight = 160 } };
            split.Add(_list); split.Add(_details); Add(split); ShowResources(false);
        }

        static VisualElement Toolbar() { var row = new VisualElement(); row.AddToClassList("analysis-toolbar"); return row; }
        static Label Text(string name) => new Label { name = name, style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, marginBottom = 4 } };
        static PopupField<BundleAnalysisRecord> Picker(string label, string name) => new PopupField<BundleAnalysisRecord>(label, new List<BundleAnalysisRecord> { null }, 0)
        {
            name = name, formatSelectedValueCallback = record => record?.ToString() ?? "尚无历史结果",
            formatListItemCallback = record => record?.ToString() ?? "尚无历史结果",
            style = { flexGrow = 1, flexShrink = 1, flexBasis = 0, minWidth = 0 }
        };
        void Column(string name, string title, int width, Func<BundleComparisonRow, string> caption)
        {
            var column = new Column { name = name, title = title, width = width, minWidth = 75, stretchable = name == "name" };
            column.makeCell = () => new Label();
            column.bindCell = (cell, index) => { var text = caption(_rows[index]); ((Label)cell).text = text; cell.tooltip = text;
                cell.style.overflow = Overflow.Hidden; cell.style.textOverflow = TextOverflow.Ellipsis; };
            _list.columns.Add(column);
        }
        internal void RefreshRecords()
        {
            var records = _cache.Records().ToList();
            var before = records.FirstOrDefault(record => record.Id == _before.value?.Id) ?? records.Skip(1).FirstOrDefault() ?? records.FirstOrDefault();
            var after = records.FirstOrDefault(record => record.Id == _after.value?.Id) ?? records.FirstOrDefault();
            _before.choices = _after.choices = records.Count == 0 ? new List<BundleAnalysisRecord> { null } : records;
            _before.SetValueWithoutNotify(before); _after.SetValueWithoutNotify(after);
            _notice.text = (string.IsNullOrEmpty(_cache.Notice) ? "" : _cache.Notice + "\n") +
                (records.Count < 2 ? "请先完成两份不同 report 的分析，再选择基线与目标。" : "选择两份完整结果，点击比较。");
            SetBusy(_busy);
        }
        internal void SetBusy(bool busy)
        {
            _busy = busy; _before.SetEnabled(!busy); _after.SetEnabled(!busy);
            _compare.SetEnabled(!busy && _before.value != null && _after.value != null && _before.value.Id != _after.value.Id);
        }
        void SelectionChanged()
        {
            Comparison = null; _details.Clear(); _summary.text = ""; _notice.text = "选择已变化，点击比较更新结果。"; SetBusy(_busy); Rebuild();
        }
        internal void Compare()
        {
            if (_busy) return;
            try
            {
                if (_before.value == null || _after.value == null || _before.value.Id == _after.value.Id)
                    throw new InvalidOperationException("请选择两份不同的历史报告。");
                var before = _cache.LoadInspection(_before.value.Id); var after = _cache.LoadInspection(_after.value.Id);
                Comparison = new BundleAnalysisComparison(before, after);
                _summary.text = $"{_before.value.reportName} → {_after.value.reportName}\nAB：{before.Packages.Report.Bundles.Count} → {after.Packages.Report.Bundles.Count}；包总大小 {BundleAnalysisFormatting.Size(before.Packages.Report.TotalBytes)} → {BundleAnalysisFormatting.Size(after.Packages.Report.TotalBytes)}（{Signed(after.Packages.Report.TotalBytes - before.Packages.Report.TotalBytes)}）；资源 {before.Resources.Length} → {after.Resources.Length}";
                _notice.text = Comparison.Notice; _details.Clear(); Rebuild();
            }
            catch (Exception exception) when (BundleAnalysisCache.IsReadError(exception) || exception is InvalidOperationException)
            { Comparison = null; _summary.text = "比较未完成"; _notice.text = exception.Message; _details.Clear(); Rebuild(); }
        }
        internal void ShowResources(bool resources)
        {
            _resourceMode = resources; _packages.EnableInClassList("active", !resources); _resources.EnableInClassList("active", resources);
            _details.Clear(); Rebuild();
        }
        void Rebuild()
        {
            var source = Comparison == null ? Array.Empty<BundleComparisonRow>() : _resourceMode ? Comparison.Resources : Comparison.Packages;
            _rows = source.Where(row => (!_changed.value || row.Changed) &&
                (row.Name + " " + row.Change).IndexOf(_search.value ?? "", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(row => Math.Abs(row.Delta ?? 0)).ThenBy(row => row.Name, StringComparer.Ordinal).ToList();
            _list.ClearSelection(); _details.Clear(); _list.itemsSource = _rows; _list.RefreshItems();
        }
        internal static string Signed(long bytes) => bytes == 0 ? "0 B" : (bytes > 0 ? "+" : "−") + BundleAnalysisFormatting.Size(Math.Abs(bytes));
        internal static string SizeCaption(bool exists, long? bytes, AssetBundleSizeCoverage coverage) => !exists ? "未收录" : !bytes.HasValue ? "未统计" :
            BundleAnalysisFormatting.Size(bytes.Value) + (coverage == AssetBundleSizeCoverage.Partial ? "（部分）" : "");
    }
}
