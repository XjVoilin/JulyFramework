using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal sealed class AssetCheckResultsView : VisualElement
    {
        private sealed class RuleChoice
        {
            internal string Id, Name;
            public override string ToString() => Name;
        }
        internal readonly AssetCheckResultsModel Model = new AssetCheckResultsModel();
        private readonly MultiColumnListView _list;
        private readonly VisualElement _controls;
        private readonly PopupField<RuleChoice> _filter;
        private readonly Label _counts;
        private readonly Label _empty;
        private readonly Dictionary<AssetCheckSort, Button> _headers = new Dictionary<AssetCheckSort, Button>();
        private readonly Action<AssetCheckIssue[]> _fix;
        private readonly Action<AssetCheckIssue> _ignore;
        private readonly Action<AssetCheckIssue, bool> _locate;
        private bool _running;
        private AssetCheckReport _report;
        internal event Action SelectionChanged;
        private bool CanModify => !_running && Model.CanModify;
        internal AssetCheckIssue[] AllFixes => Model.AllFixable();
        internal AssetCheckIssue[] SelectedFixes => Model.Fixable(true);

        internal AssetCheckResultsView(Action<AssetCheckIssue[]> fix, Action<AssetCheckIssue> ignore,
            Action<AssetCheckIssue, bool> locate)
        {
            _fix = fix; _ignore = ignore; _locate = locate;
            style.flexGrow = 1;
            style.minHeight = 120;
            _controls = new VisualElement();
            _controls.AddToClassList("result-controls");
            var all = new RuleChoice { Name = "全部问题类型" };
            _filter = new PopupField<RuleChoice>("问题类型", new List<RuleChoice> { all }, all);
            _filter.RegisterValueChangedCallback(change => { Model.Filter(change.newValue.Id); Render(); });
            _controls.Add(_filter);
            _controls.Add(new Button(() => { Model.ExpandAll(true); Render(); }) { text = "展开全部", tooltip = "展开全部问题，包括仅未验证文件" });
            _controls.Add(new Button(() => { Model.ExpandAll(false); Render(); }) { text = "折叠全部" });
            _counts = new Label();
            _counts.AddToClassList("result-counts");
            _controls.Add(_counts);
            Add(_controls);
            _list = new MultiColumnListView
            {
                selectionType = SelectionType.None, fixedItemHeight = 44,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly
            };
            _list.AddToClassList("results");
            _list.columns.Add(new Column
            {
                name = "resource", title = "文件／对象", width = 370, minWidth = 250, stretchable = true,
                makeHeader = () => MakeHeader("resource", "文件", AssetCheckSort.FileName),
                makeCell = MakeResource, bindCell = (cell, index) => BindResource(cell, Model.Rows[index]),
                unbindCell = (cell, index) => cell.Q<AssetCheckThumbnail>().ResetPreview()
            });
            TextColumn("problem", "问题", 180, AssetCheckSort.Problem, Problem);
            TextColumn("actual", "当前值", 145, AssetCheckSort.Actual, row => Value(row, false));
            TextColumn("expected", "允许／要求", 140, AssetCheckSort.Expected, row => Value(row, true));
            _list.columns.Add(new Column
            {
                name = "actions", title = "操作", width = 150, minWidth = 140,
                makeCell = MakeActions, bindCell = (cell, index) => BindActions(cell, Model.Rows[index])
            });
            Add(_list);
            _empty = new Label();
            _empty.AddToClassList("empty-results");
            Add(_empty);
        }

        internal void Bind(AssetCheckReport report, bool running)
        {
            _running = running;
            _report = report;
            var changed = Model.Bind(report);
            if (changed)
            {
                var choices = new List<RuleChoice> { new RuleChoice { Name = "全部问题类型" } };
                choices.AddRange((report?.Issues ?? new List<AssetCheckIssue>()).GroupBy(issue => issue.RuleId)
                    .Select(group => new RuleChoice { Id = group.Key, Name = group.First().Problem }));
                var retainedRule = Model.Rule;
                _filter.choices = choices;
                var selected = choices.FirstOrDefault(choice => choice.Id == retainedRule) ?? choices[0];
                if (selected.Id != Model.Rule) Model.Filter(selected.Id);
                _filter.SetValueWithoutNotify(selected);
            }
            _controls.SetEnabled(!running);
            Render();
        }

        private void Render()
        {
            if (!ReferenceEquals(_list.itemsSource, Model.Rows)) _list.itemsSource = Model.Rows;
            _list.RefreshItems();
            _counts.text = $"{Model.Files.Count:N0} / {Model.TotalAttentionFiles:N0} 个文件";
            _counts.tooltip = Model.FilterDescription + "\n勾选按文件操作；修改选中遵循筛选，修改全部覆盖当前类型。折叠不改变修改范围。";
            foreach (var pair in _headers)
            {
                pair.Value.text = (string)pair.Value.userData + (pair.Key == Model.Sort ? Model.Descending ? "  ▼" : "  ▲" : "");
                pair.Value.SetEnabled(!_running);
                pair.Value.EnableInClassList("sorted", pair.Key == Model.Sort);
            }
            var empty = Model.Rows.Count == 0;
            _list.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            _empty.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.text = _report == null ? "开始检查，查看当前类型的资源问题。" : Model.TotalAttentionFiles > 0 ?
                "当前问题类型下没有结果。" : _report.Complete ? "检查完成，没有发现问题。" : "暂无结果，请完成检查。";
            SelectionChanged?.Invoke();
        }

        private VisualElement MakeResource()
        {
            var cell = new VisualElement(); cell.AddToClassList("resource-cell");
            var selected = new Toggle { name = "select" };
            selected.RegisterValueChangedCallback(change =>
            {
                var row = (AssetCheckDisplayRow)cell.userData;
                if (change.newValue) Model.Selected.Add(row.File.Id); else Model.Selected.Remove(row.File.Id);
                SelectionChanged?.Invoke();
            });
            cell.Add(selected);
            cell.Add(new Button(() =>
            {
                var row = (AssetCheckDisplayRow)cell.userData;
                if (row.UnknownHeader) { Model.ShowUnknown = !Model.ShowUnknown; Model.RebuildRows(); }
                else Model.Toggle(row.File.Id);
                Render();
            }) { name = "expand" });
            EventCallback<ClickEvent> locate = evt =>
            {
                var row = (AssetCheckDisplayRow)cell.userData;
                if (evt.button == 0 && !row.UnknownHeader)
                    _locate(row.Issue ?? row.File.AllIssues[0], row.Issue != null);
            };
            var thumbnail = new AssetCheckThumbnail();
            thumbnail.RegisterCallback(locate);
            cell.Add(thumbnail);
            var text = new VisualElement(); text.AddToClassList("resource-text");
            text.RegisterCallback(locate);
            text.Add(new Label { name = "name" });
            var path = new Label { name = "path" };
            path.style.unityTextOverflowPosition = TextOverflowPosition.Start;
            text.Add(path);
            cell.Add(text);
            return cell;
        }

        private void BindResource(VisualElement cell, AssetCheckDisplayRow row)
        {
            cell.userData = row;
            cell.EnableInClassList("child", row.Issue != null);
            cell.EnableInClassList("unknown-group", row.UnknownHeader);
            var supportsPreview = !row.UnknownHeader && AssetCheckThumbnail.Supports(row.File.AllIssues[0].Kind);
            cell.EnableInClassList("preview-row", supportsPreview);
            cell.Q<AssetCheckThumbnail>().Bind(row.IsFile && supportsPreview ? row.File.Id : null);
            var selected = cell.Q<Toggle>("select");
            selected.style.display = row.IsFile ? DisplayStyle.Flex : DisplayStyle.None;
            selected.SetValueWithoutNotify(row.IsFile && Model.Selected.Contains(row.File.Id));
            selected.SetEnabled(CanModify && row.IsFile && row.File.VisibleIssues.Any(issue => issue.ConfirmedFix() != null));
            var expand = cell.Q<Button>("expand");
            var expandable = row.UnknownHeader || row.IsFile && row.File.VisibleIssues.Length > 1;
            expand.style.visibility = expandable ? Visibility.Visible : Visibility.Hidden;
            expand.text = (row.UnknownHeader ? Model.ShowUnknown : row.IsFile && Model.Expanded.Contains(row.File.Id)) ? "▼" : "▶";
            cell.Q<Label>("name").text = row.UnknownHeader ? $"仅未验证（{Model.UnknownFiles} 个文件）" :
                row.IsFile ? row.File.Name : AssetCheckResultsModel.Subject(row.Issue);
            cell.Q<Label>("path").text = row.UnknownHeader ? "未验证不等于问题；展开后可明确设为标准。" : row.IsFile
                ? (row.File.VisibleIssues.Length == 1 && AssetCheckResultsModel.Subject(row.File.VisibleIssues[0]) != "资源设置"
                    ? AssetCheckResultsModel.Subject(row.File.VisibleIssues[0]) + " · " + row.File.DirectoryLabel : row.File.DirectoryLabel) : "";
            cell.Q<Label>("path").tooltip = row.UnknownHeader ? "未验证不等于问题。" : row.File.Path;
            cell.tooltip = row.UnknownHeader ? "当前编辑器尚未确认这些资源是否符合标准。" :
                row.IsFile ? row.File.VisibleIssues.Length == 1 ? row.File.VisibleIssues[0].ResourceName : row.File.Path : row.Issue.ResourceName;
        }

        private VisualElement MakeHeader(string name, string title, AssetCheckSort column)
        {
            var header = new Button(() => { Model.SortBy(column); Render(); })
            {
                name = "sort-" + name, userData = title,
                text = title + (column == Model.Sort ? Model.Descending ? "  ▼" : "  ▲" : ""),
                tooltip = "点击按此列排序，再次点击切换升降序。数值按大小排序；同一文件的问题保持分组，多问题文件按本列最小值排序。"
            };
            header.AddToClassList("column-sort");
            header.EnableInClassList("sorted", column == Model.Sort);
            header.SetEnabled(!_running);
            _headers[column] = header;
            return header;
        }

        private void TextColumn(string name, string title, int width, AssetCheckSort sort, Func<AssetCheckDisplayRow, string> read)
        {
            _list.columns.Add(new Column
            {
                name = name, title = title, width = width, minWidth = 80,
                makeHeader = () => MakeHeader(name, title, sort),
                makeCell = () => { var label = new Label(); label.AddToClassList("cell"); return label; },
                bindCell = (element, index) => { var text = read(Model.Rows[index]); ((Label)element).text = text; element.tooltip = text; }
            });
        }

        private static string Problem(AssetCheckDisplayRow row)
        {
            if (row.UnknownHeader) return "";
            if (row.IsFile && row.File.VisibleIssues.Length > 1)
                return (row.File.VisibleIssues.Length == row.File.AllIssues.Length ? $"{row.File.AllIssues.Length} 项：" :
                    $"{row.File.VisibleIssues.Length}/{row.File.AllIssues.Length} 项：") +
                    string.Join("、", row.File.VisibleIssues.Select(issue => issue.Problem).Distinct());
            var issue = row.Issue ?? row.File.VisibleIssues[0];
            var count = row.IsFile && row.File.VisibleIssues.Length != row.File.AllIssues.Length
                ? $"{row.File.VisibleIssues.Length}/{row.File.AllIssues.Length} 项 · " : "";
            return count + issue.Problem + (issue.Unverified ? issue.HasProblem ? "（含未验证目标）" : "（未验证）" : "");
        }

        private static string Value(AssetCheckDisplayRow row, bool expected)
        {
            if (row.UnknownHeader || row.IsFile && row.File.VisibleIssues.Length != 1) return "";
            var issue = row.Issue ?? row.File.VisibleIssues[0];
            return expected ? issue.Expected : issue.Actual;
        }

        private VisualElement MakeActions()
        {
            var cell = new VisualElement(); cell.AddToClassList("row-actions");
            cell.Add(new Button(() =>
            {
                var row = (AssetCheckDisplayRow)cell.userData;
                var issues = row.Issues.Select(issue => issue.ConfirmedFix()).Where(issue => issue != null).ToArray();
                if (issues.Length == 0) issues = row.Issues.Select(issue => issue.UnverifiedFix()).Where(issue => issue != null).ToArray();
                _fix(issues);
            }) { name = "fix" });
            cell.Add(new Button(() =>
            {
                var row = (AssetCheckDisplayRow)cell.userData;
                _ignore(row.File.AllIssues[0]);
            }) { name = "ignore", text = "忽略", tooltip = "忽略整个文件，该文件不再参与任何规则的扫描。" });
            cell.AddManipulator(new ContextualMenuManipulator(menu =>
            {
                var row = (AssetCheckDisplayRow)cell.userData;
                if (!row.UnknownHeader) PopulateMenu(menu.menu, row);
            }));
            return cell;
        }

        private void BindActions(VisualElement cell, AssetCheckDisplayRow row)
        {
            cell.userData = row;
            cell.style.display = row.UnknownHeader ? DisplayStyle.None : DisplayStyle.Flex;
            if (row.UnknownHeader) return;
            var confirmed = row.Issues.Select(issue => issue.ConfirmedFix()).Count(issue => issue != null);
            var unknown = row.Issues.Select(issue => issue.UnverifiedFix()).Count(issue => issue != null);
            var fix = cell.Q<Button>("fix");
            fix.style.display = confirmed + unknown > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            fix.text = confirmed > 0 ? "修改 " + confirmed + " 项" : "设为标准";
            fix.tooltip = confirmed > 0 ? "只修改已确认问题；未验证目标可通过右键菜单明确设为标准。" : "明确将未验证目标写为修改目标格式。";
            fix.SetEnabled(CanModify);
            var ignore = cell.Q<Button>("ignore");
            ignore.style.display = row.IsFile ? DisplayStyle.Flex : DisplayStyle.None;
            ignore.SetEnabled(!_running);
        }

        private void PopulateMenu(DropdownMenu menu, AssetCheckDisplayRow row)
        {
            if (row.IsFile) menu.AppendAction("忽略整个文件", _ => _ignore(row.File.AllIssues[0]),
                !_running ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            var unknown = row.Issues.Select(issue => issue.UnverifiedFix()).Where(issue => issue != null).ToArray();
            if (unknown.Length > 0) menu.AppendAction("将未验证目标设为标准", _ => _fix(unknown),
                CanModify ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }
    }
}
