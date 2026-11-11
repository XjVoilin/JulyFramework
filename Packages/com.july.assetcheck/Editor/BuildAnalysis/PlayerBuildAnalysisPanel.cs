using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal sealed class PlayerBuildAnalysisPanel : VisualElement
    {
        private readonly Label _metadata, _buildSize, _assetSize, _assetCount, _counts, _empty, _error;
        private readonly TextField _search;
        private readonly MultiColumnListView _list;
        private readonly VisualElement _metrics;
        private PlayerBuildSnapshot _snapshot;
        private List<PlayerBuildAsset> _rows = new List<PlayerBuildAsset>();

        internal PlayerBuildAnalysisPanel()
        {
            name = "player-build-analysis-page";
            AddToClassList("player-build-analysis");
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.assetcheck/Editor/BuildAnalysis/PlayerBuildAnalysisPanel.uss"));
            var header = Element("player-build-heading");
            var title = Element("player-build-title-group");
            title.Add(new Label("客户端构建资源") { name = "player-build-title" });
            title.Add(new Label("查看最近一次包含资源明细的客户端构建，按占用空间排序。") { name = "player-build-subtitle" });
            header.Add(title);
            var refresh = new Button(Activate) { text = "刷新记录", tooltip = "读取已保存的构建结果，不发起构建。" };
            header.Add(refresh); Add(header);

            _metrics = Element("player-build-metrics");
            _buildSize = Metric(_metrics, "Unity 构建总大小");
            _assetSize = Metric(_metrics, "已统计资源 · 未压缩");
            _assetCount = Metric(_metrics, "资源数量");
            Add(_metrics);
            _metadata = new Label { name = "player-build-metadata" }; Add(_metadata);

            var tools = Element("player-build-tools");
            _search = new TextField { name = "player-build-search", tooltip = "按资源名称、路径或类型搜索。" };
            _search.label = "搜索";
            _search.RegisterValueChangedCallback(_ => Rebuild());
            tools.Add(_search);
            tools.Add(new Button(() => _search.value = "") { text = "清除" });
            _counts = new Label { name = "player-build-counts" }; tools.Add(_counts); Add(tools);

            _list = new MultiColumnListView
            {
                name = "player-build-assets", fixedItemHeight = 44, selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = false, reorderable = false
            };
            var resource = new Column
            {
                name = "resource", title = "资源 · 点击定位", width = 480, minWidth = 260, stretchable = true,
                makeCell = () =>
                {
                    var button = new Button { name = "player-build-resource" };
                    var preview = Element("player-build-preview");
                    preview.Add(new AssetCheckThumbnail());
                    button.Add(preview);
                    var text = Element("player-build-resource-text");
                    text.Add(new Label { name = "asset-name" });
                    text.Add(new Label { name = "asset-path" });
                    button.Add(text);
                    button.clicked += () => Locate((PlayerBuildAsset)button.userData);
                    return button;
                },
                bindCell = (element, index) =>
                {
                    var asset = _rows[index]; element.userData = asset; element.tooltip = asset.path;
                    element.Q<AssetCheckThumbnail>().BindPath(ResourcePath(asset));
                    element.Q<Label>("asset-name").text = Path.GetFileName(asset.path);
                    element.Q<Label>("asset-path").text = asset.path;
                },
                unbindCell = (element, index) => element.Q<AssetCheckThumbnail>().ResetPreview()
            };
            _list.columns.Add(resource);
            Column("type", "类型", 155, asset => asset.type);
            Column("size", "入包大小 ↓", 120, asset => Size(asset.bytes));
            Column("share", "资源占比", 100, asset => _snapshot.assetBytes == 0 ? "—" : (asset.bytes * 100d / _snapshot.assetBytes).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            Add(_list);
            _empty = new Label { name = "player-build-empty" }; Add(_empty);
            var note = new Label("资源大小为未压缩合计，占比以全部已统计资源为基数。增量构建复用资源缓存时保留上次完整记录；不代表最终平台首包或下载大小。") { name = "player-build-note" };
            Add(note);
            _error = new Label { name = "player-build-error" }; Add(_error);
            RegisterCallback<AttachToPanelEvent>(_ => { PlayerBuildReportStore.Changed += Activate; Activate(); });
            RegisterCallback<DetachFromPanelEvent>(_ => PlayerBuildReportStore.Changed -= Activate);
        }

        internal void Activate()
        {
            try
            {
                var snapshot = PlayerBuildReportStore.Load();
                _snapshot = snapshot;
                _error.text = "";
                Refresh();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                _snapshot = null;
                Refresh();
                _empty.text = "无法读取构建记录";
                _error.text = exception.Message;
            }
        }

        private void Refresh()
        {
            _metrics.style.display = _snapshot == null ? DisplayStyle.None : DisplayStyle.Flex;
            _metadata.text = _snapshot == null ? "来源：Unity BuildReport。正常完成一次客户端构建后，自动记录到本项目。" :
                _snapshot.platform + "  /  " + (DateTimeOffset.TryParse(_snapshot.builtAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : _snapshot.builtAt) +
                "  /  Unity " + _snapshot.unityVersion + "  /  " + (_snapshot.development ? "Development" : "Release");
            _metadata.tooltip = _snapshot?.outputPath ?? PlayerBuildReportStore.RelativePath;
            if (_snapshot != null)
            {
                _buildSize.text = Size(_snapshot.totalBytes); _assetSize.text = Size(_snapshot.assetBytes);
                _assetCount.text = _snapshot.assets.Length.ToString("N0");
                _buildSize.tooltip = _snapshot.totalBytes.ToString("N0") + " 字节；Unity 报告值，不等于平台最终发布包大小。";
                _assetSize.tooltip = _snapshot.assetBytes.ToString("N0") + " 字节；仅汇总报告提供的资源数据。";
            }
            _search.SetEnabled(_snapshot != null);
            Rebuild();
        }

        private void Rebuild()
        {
            var query = _search.value?.Trim() ?? "";
            _rows = _snapshot == null ? new List<PlayerBuildAsset>() : _snapshot.assets.Where(asset =>
                asset.path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (asset.type ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(asset => asset.bytes).ThenBy(asset => asset.path, StringComparer.Ordinal).ToList();
            _list.itemsSource = _rows; _list.Rebuild();
            _counts.text = _snapshot == null ? "" : $"显示 {_rows.Count:N0} / {_snapshot.assets.Length:N0} 项";
            _list.style.display = _rows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.style.display = _rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.text = _snapshot == null || _snapshot.assets.Length == 0 ?
                "暂无构建资源明细\n请执行一次完整资源构建（Clean Build）。\n普通增量构建可能只更新脚本，DetailedBuildReport 也不会补出被复用的资源数据。" : "没有匹配的资源，请修改搜索内容。";
        }

        private void Locate(PlayerBuildAsset asset)
        {
            var path = ResourcePath(asset);
            var target = AssetDatabase.LoadMainAssetAtPath(path);
            if (target == null)
            {
                _error.text = "无法定位资源：" + asset.path + "。资源可能已删除，或属于 Unity 内部生成数据。";
                return;
            }
            _error.text = "";
            Selection.activeObject = target; EditorGUIUtility.PingObject(target);
        }

        private void Column(string name, string title, int width, Func<PlayerBuildAsset, string> text)
        {
            _list.columns.Add(new Column
            {
                name = name, title = title, width = width, minWidth = 75,
                makeCell = () => { var label = new Label(); label.AddToClassList("player-build-cell"); label.AddToClassList("player-build-" + name); return label; },
                bindCell = (element, index) =>
                {
                    var asset = _rows[index]; ((Label)element).text = text(asset);
                    element.tooltip = name == "size" ? asset.bytes.ToString("N0") + " 字节（未压缩）" : text(asset);
                }
            });
        }

        private static VisualElement Element(string name) => new VisualElement { name = name };
        private static string ResourcePath(PlayerBuildAsset asset) => string.IsNullOrEmpty(asset.guid) ? asset.path : AssetDatabase.GUIDToAssetPath(asset.guid);
        private static Label Metric(VisualElement parent, string caption)
        {
            var metric = Element("player-build-metric");
            var value = new Label("—"); value.AddToClassList("player-build-metric-value"); metric.Add(value);
            var label = new Label(caption); label.AddToClassList("player-build-metric-caption"); metric.Add(label);
            parent.Add(metric); return value;
        }
        private static string Size(ulong bytes) => bytes >= 1048576 ? (bytes / 1048576d).ToString("N2") + " MiB" :
            bytes >= 1024 ? (bytes / 1024d).ToString("N1") + " KiB" : bytes + " B";
    }
}
