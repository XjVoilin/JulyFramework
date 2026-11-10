using System;
using System.Linq;
using July.Logging;
using TMPro;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.AssetCheck
{
    internal sealed class FontCheckPanel : VisualElement, IDisposable
    {
        private readonly Func<AssetCheckWorkspace> _workspace;
        private readonly Action _changed;
        private readonly FontCheckSession _session;
        private readonly ObjectField _font, _characterFile;
        private readonly VisualElement _inputs;
        private readonly ListView _sources;
        private readonly Foldout _sourceSection;
        private readonly Label _status, _error;
        private readonly Button _check, _cancel;
        private readonly ProgressBar _progress;
        private readonly VisualElement _results;
        private string _settingsKey;
        private FontCheckResult _shownResult;
        private bool _shownStale;
        private bool _active;
        private Button _copyMissing;
        internal bool Running => _session.Running;
        internal FontCheckSession Session => _session;

        internal FontCheckPanel(Func<AssetCheckWorkspace> workspace, Action changed)
        {
            _workspace = workspace; _changed = changed;
            _session = new FontCheckSession(workspace, StateChanged);
            name = "font-check-page";
            style.flexGrow = 1;
            var scroll = new ScrollView { style = { flexGrow = 1, paddingLeft = 12, paddingRight = 12, paddingTop = 8 } };
            Add(scroll);
            scroll.Add(new HelpBox("从 Assets 自动发现文件名包含 language 的 JSON，可添加或移除来源。" +
                "递归收集所有字符串值（包含 Key、Id 等字符串），不收集属性名，忽略换行等控制符。" +
                "字体只检查 TMP 默认字体已包含的字符，不向动态字体补字，也不计入备用字体。", HelpBoxMessageType.Info));
            _inputs = new VisualElement(); scroll.Add(_inputs);
            _font = new ObjectField("TMP 默认字体") { name = "font-target", objectType = typeof(TMP_FontAsset), allowSceneObjects = false };
            _font.SetEnabled(false); _inputs.Add(_font);
            _characterFile = new ObjectField("字符文件") { name = "font-character-file", objectType = typeof(TextAsset), allowSceneObjects = false };
            _characterFile.RegisterValueChangedCallback(change => RunUi(() => _session.SetCharacterFile(AssetDatabase.GetAssetPath(change.newValue))));
            _inputs.Add(_characterFile);
            _sourceSection = new Foldout { name = "font-sources-section", text = "文案来源", value = true };
            _inputs.Add(_sourceSection);
            var sourceTools = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            sourceTools.Add(new Button(() => RunUi(_session.RefreshSources)) { name = "font-discover", text = "刷新来源", tooltip = "发现新 language JSON，保留已移除项；手动添加可重新纳入。" });
            sourceTools.Add(new Button(() => RunUi(() =>
            {
                var path = EditorUtility.OpenFilePanel("添加 JSON 文件", Application.dataPath, "json");
                if (!string.IsNullOrEmpty(path)) _session.AddSource(FileUtil.GetProjectRelativePath(path));
            })) { name = "font-add-source", text = "添加 JSON 文件" });
            _sourceSection.Add(sourceTools);
            _sources = new ListView { name = "font-sources", fixedItemHeight = 28, selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, makeItem = MakeSourceRow };
            _sources.bindItem = (row, index) =>
            {
                var guid = (string)_sources.itemsSource[index];
                var path = AssetDatabase.GUIDToAssetPath(guid);
                row.userData = guid;
                var locate = row.Q<Button>("source-path");
                locate.text = string.IsNullOrEmpty(path) ? "已移除资源：" + guid : path;
                locate.tooltip = locate.text;
                locate.SetEnabled(!string.IsNullOrEmpty(path));
            };
            _sourceSection.Add(_sources);
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8, marginBottom = 6 } };
            _check = new Button(() => RunUi(_session.Start)) { name = "font-check", text = "检查字体" };
            _cancel = new Button(_session.Cancel) { name = "font-cancel", text = "取消" };
            actions.Add(_check); actions.Add(_cancel); scroll.Add(actions);
            _progress = new ProgressBar { lowValue = 0, highValue = 1 }; scroll.Add(_progress);
            _status = new Label { name = "font-status", style = { whiteSpace = WhiteSpace.Normal, marginBottom = 6 } }; scroll.Add(_status);
            _error = new Label { name = "font-error", style = { whiteSpace = WhiteSpace.Normal } };
            _error.AddToClassList("operation-error"); scroll.Add(_error);
            _results = new VisualElement { name = "font-results" }; scroll.Add(_results);
            Refresh();
        }

        internal void Activate()
        {
            _active = true;
            if (!_session.Settings.initialized) RunUi(_session.RefreshSources);
            Refresh();
        }

        private VisualElement MakeSourceRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var locate = new Button(() => Locate(AssetDatabase.GUIDToAssetPath((string)row.userData)))
            { name = "source-path", style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } };
            var remove = new Button(() => RunUi(() => _session.RemoveSource((string)row.userData))) { text = "移除" };
            row.Add(locate); row.Add(remove); return row;
        }

        internal void Pump() => _session.Pump();
        internal void SynchronizeSettings() { _session.SynchronizeSettings(); Refresh(); }

        private void StateChanged() { Refresh(); _changed(); }

        private void Refresh()
        {
            var settings = _session.Settings;
            var key = JsonUtility.ToJson(settings) + "|" + string.Join("|", settings.sourceGuids.Select(AssetDatabase.GUIDToAssetPath));
            var font = _active ? _session.TargetFont : null;
            _font.SetValueWithoutNotify(font);
            _font.tooltip = AssetDatabase.GetAssetPath(font);
            _characterFile.SetValueWithoutNotify(AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(settings.characterFileGuid)));
            if (_settingsKey != key)
            {
                _settingsKey = key;
                _sources.itemsSource = settings.sourceGuids.ToArray();
                _sources.style.height = Math.Max(1, Math.Min(settings.sourceGuids.Count, 6)) * 28;
                _sources.Rebuild();
                _sourceSection.text = $"文案来源（{settings.sourceGuids.Count} 个；移除后刷新不会重新加入）";
            }
            _inputs.SetEnabled(!Running && !_workspace().Running);
            _check.SetEnabled(!Running && !_workspace().Running);
            _cancel.style.display = Running ? DisplayStyle.Flex : DisplayStyle.None;
            _progress.style.display = Running ? DisplayStyle.Flex : DisplayStyle.None;
            _progress.value = _session.Progress;
            _status.text = _session.Status + (_session.Result == null ? "" : "\n检查时间：" + _session.Result.CheckedAt);
            if (_session.Failure != null) _error.text = _session.Failure;
            _error.style.display = string.IsNullOrEmpty(_error.text) ? DisplayStyle.None : DisplayStyle.Flex;
            if (_shownResult != _session.Result || _shownStale != _session.Stale)
            {
                _shownResult = _session.Result; _shownStale = _session.Stale;
                _results.Clear(); _copyMissing = null;
                if (_shownResult != null) AddResult(_shownResult);
            }
            _copyMissing?.SetEnabled(!_session.Stale && !Running && _shownResult.MissingCharacters.Length > 0);
        }

        private void AddResult(FontCheckResult result)
        {
            var missing = result.MissingCharacters;
            var group = new VisualElement { style = { marginTop = 6, marginBottom = 8 } };
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            header.Add(new Label($"缺失字符（{missing.Length} 个）") { name = "font-missing-count", style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });
            _copyMissing = new Button(() => RunUi(_session.CopyMissing))
            { name = "font-copy-missing", text = "复制缺失字符" };
            header.Add(_copyMissing); group.Add(header);
            if (missing.Length == 0) group.Add(new Label("字符覆盖完整"));
            else
            {
                var characters = new TextField { name = "font-missing",
                    multiline = true, isReadOnly = true, value = FontCheckResult.CopyText(missing),
                    style = { minHeight = 40, maxHeight = 120 } };
                group.Add(characters);
                group.Add(new Label(ResultHint(result)) { name = "font-missing-hint", style = { whiteSpace = WhiteSpace.Normal } });
                var paths = missing.SelectMany(entry => entry.SourcePaths).Distinct().OrderBy(path => path, StringComparer.Ordinal).ToArray();
                var sources = new Foldout { name = "font-missing-sources", text = $"查看来源（{paths.Length} 个文件）", value = false };
                foreach (var path in paths)
                {
                    var entries = missing.Where(entry => entry.SourcePaths.Contains(path)).ToArray();
                    var source = new Foldout { text = path + $"（{entries.Length} 个缺字）", value = false };
                    source.Add(new Button(() => Locate(path)) { name = "font-locate-missing-source", text = "定位 JSON 文件", tooltip = path });
                    source.Add(new TextField { name = "font-source-missing", multiline = true, isReadOnly = true,
                        value = FontCheckResult.CopyText(entries), style = { minHeight = 30, maxHeight = 120 } });
                    sources.Add(source);
                }
                group.Add(sources);
            }
            _results.Add(group);
        }

        private static string ResultHint(FontCheckResult result)
        {
            if (result.CharacterFileMissing.Length == 0) return "字符文件已覆盖上述字符，重新构建字体后再检查。";
            if (result.FontMissing.Length == 0) return "字体已覆盖上述字符，请补齐配置并重新导出字符文件。";
            if (result.CharacterFileMissing.Select(entry => entry.Unicode).SequenceEqual(result.FontMissing.Select(entry => entry.Unicode)))
                return "字符文件和字体均缺少上述字符。补齐配置并导出后，重新构建字体。";
            return $"字符文件缺失 {result.CharacterFileMissing.Length} 个，字体缺失 {result.FontMissing.Length} 个；上方已合并去重。";
        }

        private static void Locate(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null) { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
        }

        private void RunUi(Action action)
        {
            try { _error.text = ""; action(); }
            catch (Exception exception) { _error.text = exception.Message; JLogger.LogException(exception); }
            Refresh(); _changed();
        }

        public void Dispose() => _session.Dispose();
    }
}
