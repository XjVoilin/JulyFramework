using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using July.Logging;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal sealed class WebGLTextureSettingsCopyPanel : VisualElement, IDisposable
    {
        private readonly Func<AssetCheckWorkspace> _workspace;
        private readonly Action _changed;
        private string _observedScope;
        private string _observedExclusions;
        private readonly List<WebGLTextureSettingsCopyEntry> _entries = new List<WebGLTextureSettingsCopyEntry>();
        private readonly HashSet<string> _selected = new HashSet<string>();
        private string[] _scanPaths;
        private int _scanIndex, _ignoredCount;
        private bool _complete, _stale, _writing;
        private string _message = "选择文件夹后点击扫描，查看平台配置差异。";
        private string _copySummary = "";
        private WebGLTextureSettingsCopy _operation;
        private VisualElement _resultControls;
        private Button _scan;
        private MultiColumnListView _list;
        private List<WebGLTextureSettingsCopyEntry> _visible = new List<WebGLTextureSettingsCopyEntry>();
        private Label _status, _counts;
        private Toggle _onlyDifferences;
        private Button _copyAll, _copySelected, _cancel;
        private ProgressBar _progress;
        private bool Scanning => _scanPaths != null;
        private bool Copying => _operation != null && _operation.Running;
        internal bool Running => Scanning || Copying;
        private bool CanCopy => _complete && !_stale && !Running;

        internal WebGLTextureSettingsCopyPanel(Func<AssetCheckWorkspace> workspace, Action changed)
        {
            _workspace = workspace;
            _changed = changed;
            name = "platform-copy-page";
            style.flexGrow = 1;
            Build();
            AssetCheckAssetChanges.Changed += OnAssetsChanged;
        }

        public void Dispose()
        {
            AssetCheckAssetChanges.Changed -= OnAssetsChanged;
            _operation?.Cancel();
            _scanPaths = null;
            _complete = false;
        }

        private void Build()
        {
            var root = this;
            root.style.paddingLeft = root.style.paddingRight = 8;
            root.style.paddingTop = root.style.paddingBottom = 8;
            root.Add(new HelpBox("选择文件夹 → 扫描 → 查看差异 → 复制。WebGL 未启用覆盖时复制 Default 配置；" +
                "两个目标启用覆盖，Automatic 保持原值。与资源检查共用忽略名单。取消或关闭窗口保留已保存的修改。", HelpBoxMessageType.Info));
            _scan = new Button(() => RunUi(StartScan)) { name = "scan", text = "扫描平台配置" };
            root.Add(_scan);
            _resultControls = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
            _onlyDifferences = new Toggle("仅显示差异") { name = "only-differences", value = true };
            _onlyDifferences.RegisterValueChangedCallback(_ => RebuildRows());
            _resultControls.Add(_onlyDifferences);
            _resultControls.Add(new Button(() =>
            {
                foreach (var entry in _entries.Where(entry => entry.NeedsCopy)) _selected.Add(entry.Guid);
                RebuildRows();
            }) { text = "全选差异" });
            _resultControls.Add(new Button(() => { _selected.Clear(); RebuildRows(); }) { text = "清空选择" });
            _copySelected = new Button(() => RunUi(() => StartCopy(true))) { name = "copy-selected", text = "复制选中" };
            _copyAll = new Button(() => RunUi(() => StartCopy(false))) { name = "copy-all", text = "复制全部差异" };
            _resultControls.Add(_copySelected);
            _resultControls.Add(_copyAll);
            root.Add(_resultControls);
            _counts = new Label { name = "counts", style = { marginTop = 6, marginBottom = 6 } };
            root.Add(_counts);
            _list = new MultiColumnListView
            {
                name = "results", selectionType = SelectionType.None, fixedItemHeight = 48,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                style = { flexGrow = 1, minHeight = 160 }
            };
            _list.columns.Add(new Column
            {
                name = "resource", title = "资源文件", width = 340, minWidth = 260, stretchable = true,
                makeCell = MakeResource, bindCell = (cell, index) => BindResource(cell, _visible[index])
            });
            AddColumn("source", "WebGL 来源", entry => (entry.Inherited ? "继承 Default\n" : "WebGL 覆盖\n") +
                WebGLTextureSettingsCopyEntry.Summary(entry.Source), entry => WebGLTextureSettingsCopyEntry.Description(entry.Source));
            AddColumn("minigame", "MiniGame", entry => TargetText(entry.MiniGame, entry.CopyMiniGame),
                entry => WebGLTextureSettingsCopyEntry.Description(entry.MiniGame));
            AddColumn("weixin", "WeixinMiniGame", entry => TargetText(entry.WeixinMiniGame, entry.CopyWeixinMiniGame),
                entry => WebGLTextureSettingsCopyEntry.Description(entry.WeixinMiniGame));
            root.Add(_list);
            _progress = new ProgressBar();
            root.Add(_progress);
            _cancel = new Button(Cancel) { name = "cancel", text = "取消" };
            root.Add(_cancel);
            _status = new Label { name = "status", style = { whiteSpace = WhiteSpace.Normal, marginTop = 6 } };
            root.Add(_status);
            RebuildRows();
        }

        private void AddColumn(string name, string title, Func<WebGLTextureSettingsCopyEntry, string> text,
            Func<WebGLTextureSettingsCopyEntry, string> tooltip)
        {
            _list.columns.Add(new Column
            {
                name = name, title = title, width = 225, minWidth = 180, stretchable = true,
                makeCell = () =>
                {
                    var cell = new Label { style = { whiteSpace = WhiteSpace.Normal, unityTextAlign = TextAnchor.MiddleLeft } };
                    RegisterLocate(cell);
                    return cell;
                },
                bindCell = (cell, index) =>
                {
                    cell.userData = _visible[index];
                    ((Label)cell).text = text(_visible[index]);
                    cell.tooltip = tooltip(_visible[index]);
                }
            });
        }

        private static string TargetText(TextureImporterPlatformSettings settings, bool needsCopy) =>
            (needsCopy ? "待复制" : "一致") + (settings.overridden ? "\n" : "（未启用覆盖）\n") + WebGLTextureSettingsCopyEntry.Summary(settings);

        private VisualElement MakeResource()
        {
            var cell = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, overflow = Overflow.Hidden } };
            RegisterLocate(cell);
            var toggle = new Toggle();
            toggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            toggle.RegisterValueChangedCallback(change =>
            {
                var entry = (WebGLTextureSettingsCopyEntry)cell.userData;
                if (change.newValue) _selected.Add(entry.Guid); else _selected.Remove(entry.Guid);
                Refresh();
            });
            cell.Add(toggle);
            var text = new VisualElement { style = { flexGrow = 1, minWidth = 0 } };
            text.Add(new Label { name = "filename" });
            text.Add(new Label { name = "path", style = { fontSize = 10, overflow = Overflow.Hidden, unityTextOverflowPosition = TextOverflowPosition.Start, textOverflow = TextOverflow.Ellipsis } });
            cell.Add(text);
            return cell;
        }

        private void BindResource(VisualElement cell, WebGLTextureSettingsCopyEntry entry)
        {
            cell.userData = entry;
            var toggle = cell.Q<Toggle>();
            toggle.SetValueWithoutNotify(_selected.Contains(entry.Guid));
            toggle.SetEnabled(CanCopy && entry.NeedsCopy);
            cell.Q<Label>("filename").text = Path.GetFileName(entry.Path) + (entry.IsAtlas ? "（图集）" : "");
            cell.Q<Label>("path").text = Path.GetDirectoryName(entry.Path)?.Replace('\\', '/');
            cell.tooltip = entry.Path;
        }

        private static void RegisterLocate(VisualElement cell)
        {
            cell.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button == 0) Locate((WebGLTextureSettingsCopyEntry)cell.userData);
            });
        }

        private static void Locate(WebGLTextureSettingsCopyEntry entry)
        {
            var path = AssetDatabase.GUIDToAssetPath(entry.Guid);
            if (string.IsNullOrEmpty(path)) return; // 扫描后删除的资源仍可能显示在过期列表中。
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        // 目录和忽略名单由共享 Workspace 持有；这里只保存观察值以使旧结果失效。
        internal void SynchronizeSettings()
        {
            var settings = _workspace().Settings;
            var exclusions = AssetCheckExclusions.Key(settings);
            if (_observedScope == settings.scope && _observedExclusions == exclusions) return;
            _observedScope = settings.scope;
            _observedExclusions = exclusions;
            _operation?.Cancel();
            _scanPaths = null;
            _entries.Clear();
            _selected.Clear();
            _ignoredCount = 0;
            _complete = false;
            _stale = false;
            _operation = null;
            _message = "目录或忽略名单已更新，请重新扫描。";
            RebuildRows();
        }

        internal void StartScan() => BeginScan("");

        private void BeginScan(string copySummary)
        {
            var settings = _workspace().ReadCurrentSettings();
            SynchronizeSettings();
            _scanPaths = WebGLTextureSettingsCopy.Collect(new[] { settings.scope });
            _scanIndex = 0;
            _ignoredCount = 0;
            _complete = false;
            _stale = false;
            _entries.Clear();
            _selected.Clear();
            _operation = null;
            _copySummary = copySummary;
            _message = copySummary + "扫描中，仅读取资源配置。";
            if (_scanPaths.Length == 0) FinishScan();
            RebuildRows();
        }

        private void FinishScan()
        {
            _scanPaths = null;
            _complete = true;
            _message = _copySummary + "扫描完成，可以复制全部差异或勾选文件。";
            RebuildRows();
        }

        internal void StartCopy(bool selectedOnly)
        {
            _workspace().ReadCurrentSettings();
            SynchronizeSettings();
            if (!CanCopy) throw new InvalidOperationException("请先完成当前目录的扫描。");
            var paths = _entries.Where(entry => entry.NeedsCopy && (!selectedOnly || _selected.Contains(entry.Guid)))
                .Select(entry => AssetCheckTarget.RequirePath(entry.Guid)).ToArray();
            _operation = new WebGLTextureSettingsCopy(paths, () => _workspace().ReadCurrentSettings());
            _message = "复制中。";
            Refresh();
            _list.RefreshItems();
        }

        internal void Pump()
        {
            if (!Running) return;
            RunUi(() =>
            {
                var settings = _workspace().ReadCurrentSettings();
                SynchronizeSettings();
                if (!Running) return; // 目录或忽略名单改变后，停止使用旧结果。
                if (Scanning)
                {
                    var exclusions = new AssetCheckExclusions(settings);
                    var end = Math.Min(_scanIndex + 20, _scanPaths.Length);
                    while (_scanIndex < end)
                    {
                        var path = _scanPaths[_scanIndex++];
                        if (exclusions.Contains(AssetDatabase.AssetPathToGUID(path), path)) _ignoredCount++;
                        else _entries.Add(WebGLTextureSettingsCopy.Inspect(path));
                    }
                    if (_scanIndex == _scanPaths.Length) FinishScan();
                }
                else
                {
                    _writing = true;
                    try { _operation.Step(); }
                    finally { _writing = false; }
                    if (!Copying)
                        BeginScan($"复制完成：修改 {_operation.Modified} 个文件，无变化 {_operation.Unchanged} 个，忽略 {_operation.Ignored} 个。\n");
                }
                Refresh();
            });
        }

        internal void Cancel()
        {
            if (Scanning)
            {
                _scanPaths = null;
                _complete = false;
                _message = "扫描已取消，完成扫描后才能复制。";
                RebuildRows();
            }
            else
            {
                _operation.Cancel();
                _stale = true;
                _message = $"复制已取消：已处理 {_operation.Processed}/{_operation.Total}，修改 {_operation.Modified} 个文件；请重新扫描查看结果。";
            }
            Refresh();
        }

        private void OnAssetsChanged(string[] paths)
        {
            if (_writing || (!_complete && !Scanning && !Copying) || !paths.Any(path =>
                path == _observedScope || path.StartsWith(_observedScope + "/", StringComparison.Ordinal))) return;
            _stale = true;
            _scanPaths = null;
            _operation?.Cancel();
            _message = "目录内资源发生变化，结果已过期，请重新扫描。已保存的复制结果保留。";
            if (_status != null) { Refresh(); _list.RefreshItems(); }
        }

        private void RebuildRows()
        {
            _visible = _entries.Where(entry => !_onlyDifferences.value || entry.NeedsCopy).ToList();
            _list.itemsSource = _visible;
            _list.RefreshItems();
            Refresh();
        }

        private void Refresh()
        {
            _scan.SetEnabled(!Running);
            _resultControls.SetEnabled(!Running);
            _copyAll.SetEnabled(CanCopy && _entries.Any(entry => entry.NeedsCopy));
            _copySelected.SetEnabled(CanCopy && _entries.Any(entry => entry.NeedsCopy && _selected.Contains(entry.Guid)));
            _cancel.SetEnabled(Running);
            _counts.text = $"已扫描 {_entries.Count + _ignoredCount} 个文件，忽略 {_ignoredCount} 个，差异 {_entries.Count(entry => entry.NeedsCopy)} 个，一致 {_entries.Count(entry => !entry.NeedsCopy)} 个，已选 {_selected.Count} 个。";
            _status.text = _message;
            _progress.style.display = Running ? DisplayStyle.Flex : DisplayStyle.None;
            _progress.title = Scanning ? $"扫描 {_scanIndex}/{_scanPaths.Length}" : Copying ? $"复制 {_operation.Processed}/{_operation.Total}" : "";
            _progress.value = Scanning ? 100f * _scanIndex / Math.Max(1, _scanPaths.Length) :
                Copying ? 100f * _operation.Processed / Math.Max(1, _operation.Total) : 0;
            _changed();
        }

        // 编辑器操作边界：停止任务并明确显示错误，已保存的修改保留。
        private void RunUi(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                _scanPaths = null;
                _operation?.Cancel();
                _stale = true;
                _message = "操作失败：" + exception.Message + "\n已保存的修改保留，请检查 Console 并重新扫描。";
                Refresh();
                _list.RefreshItems();
                JLogger.LogException(exception);
            }
        }
    }
}
