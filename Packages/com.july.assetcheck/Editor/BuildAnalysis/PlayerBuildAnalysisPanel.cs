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
        private readonly Label _metadata, _outputPath, _buildSize, _assetSize, _assetCount, _counts, _empty, _error, _notice;
        private readonly Button _baseline;
        private readonly PlayerBuildReportStore _store;
        private PlayerBuildCache _cache;
        private bool _showBaseline;
        private readonly TextField _search;
        private readonly MultiColumnListView _list;
        private readonly VisualElement _metrics;
        private PlayerBuildSnapshot _snapshot;
        private List<PlayerBuildAsset> _rows = new List<PlayerBuildAsset>();

        internal PlayerBuildAnalysisPanel() : this(new PlayerBuildReportStore(Directory.GetCurrentDirectory())) { }

        internal PlayerBuildAnalysisPanel(PlayerBuildReportStore store)
        {
            _store = store;
            name = "player-build-analysis-page";
            AddToClassList("player-build-analysis");
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.assetcheck/Editor/BuildAnalysis/PlayerBuildAnalysisPanel.uss"));
            var header = Element("player-build-heading");
            var title = Element("player-build-title-group");
            title.Add(new Label("客户端构建资源") { name = "player-build-title" });
            title.Add(new Label("查看最近一次可用的客户端资源明细，保留原构建时间和报告范围。") { name = "player-build-subtitle" });
            header.Add(title);
            var refresh = new Button(Activate) { text = "刷新记录", tooltip = "读取已保存的构建结果，不发起构建。" };
            header.Add(refresh); Add(header);
            _notice = new Label { name = "player-build-notice" }; Add(_notice);
            _baseline = new Button(() => { _showBaseline = !_showBaseline; Refresh(); })
                { name = "player-build-baseline", text = "查看保留的完整构建明细" };
            Add(_baseline);

            _metrics = Element("player-build-metrics");
            _buildSize = Metric(_metrics, "Unity 构建总大小");
            _assetSize = Metric(_metrics, "已统计资源 · 未压缩");
            _assetCount = Metric(_metrics, "资源数量");
            Add(_metrics);
            _metadata = new Label { name = "player-build-metadata" }; Add(_metadata);
            _outputPath = new Label { name = "player-build-output" }; Add(_outputPath);

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
            var note = new Label("大小为所选报告提供的未压缩合计，占比以该报告已统计资源为基数。增量报告可能仅含部分数据文件；不拼接不同构建的资源。完整构建也不代表统计覆盖最终平台包。缩略图来自当前工程。") { name = "player-build-note" };
            Add(note);
            _error = new Label { name = "player-build-error" }; Add(_error);
            RegisterCallback<AttachToPanelEvent>(_ => { PlayerBuildReportStore.Changed += Activate; Activate(); });
            RegisterCallback<DetachFromPanelEvent>(_ => PlayerBuildReportStore.Changed -= Activate);
        }

        internal void Activate()
        {
            try
            {
                _cache = _store.Load(EditorUserBuildSettings.activeBuildTarget.ToString());
                _error.text = "";
                Refresh();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                _snapshot = null;
                _cache = null;
                Refresh();
                _empty.text = "无法读取构建记录";
                _error.text = exception.Message;
            }
        }

        private void Refresh()
        {
            _snapshot = _showBaseline ? _cache?.baseline : _cache?.LatestDetails;
            var canSwitch = _cache?.baseline != null && _cache.incremental != null;
            if (!canSwitch) { _showBaseline = false; _snapshot = _cache?.LatestDetails; }
            _baseline.style.display = canSwitch ? DisplayStyle.Flex : DisplayStyle.None;
            _baseline.text = _showBaseline ? "返回最近报告明细" :
                _cache?.baseline?.version == 1 ? "查看升级前保留的明细" : "查看保留的完整构建明细";
            _notice.text = _cache == null || string.IsNullOrEmpty(_cache.lastBuiltAt) ? "" :
                _cache.lastHadDetails ? "最近内容构建：" + Time(_cache.lastBuiltAt) :
                "最近内容构建（" + Time(_cache.lastBuiltAt) + "）未提供资源明细；保留已有记录。";
            _metrics.style.display = _snapshot == null ? DisplayStyle.None : DisplayStyle.Flex;
            _metadata.text = _snapshot == null ? "来源：Unity BuildReport。完成一次客户端资源构建后自动记录。" :
                "明细来源：" + _snapshot.platform + "  /  " + Time(_snapshot.builtAt) +
                "  /  Unity " + _snapshot.unityVersion + "  /  " + (_snapshot.development ? "Development" : "Release") +
                "  /  " + (_snapshot.version == 1 ? "旧格式报告，覆盖范围未记录" :
                    _snapshot.cleanBuild ? "完整构建提供的明细" : "增量报告，可能仅含部分数据文件");
            _metadata.tooltip = _snapshot?.outputPath ?? PlayerBuildReportStore.RelativePath;
            _outputPath.text = _snapshot == null ? "" : "输出：" + _snapshot.outputPath;
            _outputPath.tooltip = _snapshot?.outputPath ?? "";
            _outputPath.style.display = _snapshot == null ? DisplayStyle.None : DisplayStyle.Flex;
            var hasDetails = _snapshot != null && _snapshot.assets.Length > 0;
            if (_snapshot != null)
            {
                _buildSize.text = Size(_snapshot.totalBytes);
                _assetSize.text = hasDetails ? Size(_snapshot.assetBytes) : "未统计";
                _assetCount.text = hasDetails ? _snapshot.assets.Length.ToString("N0") : "未统计";
                _buildSize.tooltip = _snapshot.totalBytes.ToString("N0") + " 字节；Unity 报告值，不等于平台最终发布包大小。";
                _assetSize.tooltip = hasDetails ? _snapshot.assetBytes.ToString("N0") + " 字节；仅汇总报告提供的资源数据。" : "本次报告未提供资源明细。";
            }
            _search.SetEnabled(hasDetails);
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
            _counts.text = _snapshot == null ? "" : _snapshot.assets.Length == 0 ? "本次明细未提供" : $"显示 {_rows.Count:N0} / {_snapshot.assets.Length:N0} 项";
            _list.style.display = _rows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.style.display = _rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.text = _snapshot == null ? "暂无可用的客户端资源明细\n本次未提供明细时会保留已有记录；首次需要通过现有入口执行完整资源构建（Clean Build）。" :
                _snapshot.assets.Length == 0 ? "本次报告未提供资源明细\nUnity 可能复用了资源缓存。需要查看资源排行时，请通过现有出包入口执行一次完整资源构建（Clean Build）。" :
                "没有匹配的资源，请修改搜索内容。";
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

        private static string Time(string value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
                ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : value;

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
