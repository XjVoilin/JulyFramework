using System;
using System.Linq;
using System.IO;
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
        private readonly ListView _sources, _folders;
        private readonly Label _characterHint, _sourceEmpty, _folderEmpty, _state, _resultNote;
        private readonly Foldout _sourceSection;
        private readonly Label _status, _error;
        private readonly Button _check, _cancel;
        private readonly ProgressBar _progress;
        private readonly VisualElement _results;
        private string _settingsKey;
        private FontCheckResult _shownResult;
        private bool _shownStale, _shownRunning;
        private bool _active;
        private Button _copyMissing;
        private string _operationFailure;
        internal bool Running => _session.Running;
        internal FontCheckSession Session => _session;

        internal FontCheckPanel(Func<AssetCheckWorkspace> workspace, Action changed)
        {
            _workspace = workspace; _changed = changed;
            _session = new FontCheckSession(workspace, StateChanged);
            name = "font-check-page";
            AddToClassList("font-check");
            EnableInClassList("font-check-dark", EditorGUIUtility.isProSkin);
            EnableInClassList("font-check-light", !EditorGUIUtility.isProSkin);
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.july.assetcheck/Editor/Fonts/FontCheckPanel.uss"));
            RegisterCallback<GeometryChangedEvent>(change => EnableInClassList("font-check-narrow", change.newRect.width < 880));

            var toolbar = Styled(new VisualElement(), "fc-toolbar");
            var heading = Styled(new VisualElement(), "fc-heading");
            heading.Add(Text("字体检查", "fc-title"));
            heading.Add(Text("核对文案与界面文字的字体覆盖", "fc-subtitle"));
            toolbar.Add(heading);
            _state = Text("待检查", "fc-state");
            toolbar.Add(_state);
            var actions = Styled(new VisualElement(), "fc-actions");
            _cancel = ActionButton("取消", _session.Cancel, "font-cancel");
            _check = ActionButton("检查字体", () => RunUi(_session.Start), "font-check");
            _check.AddToClassList("fc-primary");
            actions.Add(_cancel); actions.Add(_check); toolbar.Add(actions);
            Add(toolbar);

            var operation = Styled(new VisualElement(), "fc-operation");
            _status = Text("", "fc-status"); _status.name = "font-status";
            _progress = Styled(new ProgressBar { lowValue = 0, highValue = 1 }, "fc-progress");
            operation.Add(_status); operation.Add(_progress); Add(operation);
            _error = Text("", "fc-notice fc-error"); _error.name = "font-error"; Add(_error);

            var body = Styled(new VisualElement(), "fc-body");
            Add(body);
            var configuration = Styled(new ScrollView(ScrollViewMode.Vertical), "fc-config-scroll");
            body.Add(configuration);
            _inputs = Styled(new VisualElement(), "fc-config"); configuration.Add(_inputs);
            _inputs.Add(Text("检查配置", "fc-pane-title"));
            _inputs.Add(Text("JSON 来源与界面扫描目录分别管理。", "fc-caption fc-pane-caption"));

            var defaults = Section("默认字体", "用于 JSON，以及未指定字体的 TMP 文本。");
            _font = Styled(new ObjectField { name = "font-target", objectType = typeof(TMP_FontAsset), allowSceneObjects = false }, "fc-field");
            _font.SetEnabled(false); defaults.Add(_font); _inputs.Add(defaults);

            var json = Section("JSON 文案", "比较字符文件与默认字体的当前字符表。");
            _characterFile = Styled(new ObjectField("字符文件") { name = "font-character-file", objectType = typeof(TextAsset), allowSceneObjects = false }, "fc-field");
            _characterFile.RegisterValueChangedCallback(change => RunUi(() => _session.SetCharacterFile(AssetDatabase.GetAssetPath(change.newValue))));
            json.Add(_characterFile);
            _characterHint = Text("", "fc-caption fc-character-hint"); json.Add(_characterHint);
            _sourceSection = Styled(new Foldout { name = "font-sources-section", text = "来源文件", value = true }, "fc-input-foldout");
            var sourceTools = Styled(new VisualElement(), "fc-row fc-source-tools");
            var discover = ActionButton("刷新来源", () => RunUi(_session.RefreshSources), "font-discover");
            discover.tooltip = "发现新 language JSON，保留已移除项；手动添加可重新纳入。";
            sourceTools.Add(discover);
            sourceTools.Add(ActionButton("添加 JSON 文件", () => RunUi(() =>
            {
                var path = EditorUtility.OpenFilePanel("添加 JSON 文件", Application.dataPath, "json");
                if (!string.IsNullOrEmpty(path)) _session.AddSource(FileUtil.GetProjectRelativePath(path));
            }), "font-add-source"));
            _sourceSection.Add(sourceTools);
            _sources = PathList("font-sources", MakeSourceRow);
            _sources.bindItem = (row, index) => BindPathRow(row, (string)_sources.itemsSource[index], "source-path");
            _sourceEmpty = Text("没有 JSON 来源，可添加文件或刷新自动发现。", "fc-list-empty");
            _sourceSection.Add(_sources); _sourceSection.Add(_sourceEmpty);
            json.Add(_sourceSection); _inputs.Add(json);

            var hierarchy = Section("Prefab 与场景", "按组件实际字体检查，包含未激活对象。");
            hierarchy.Add(ActionButton("添加扫描目录", () => RunUi(() =>
            {
                var path = EditorUtility.OpenFolderPanel("添加 Prefab／场景扫描目录", Application.dataPath, "");
                if (!string.IsNullOrEmpty(path)) _session.AddScanFolder(FileUtil.GetProjectRelativePath(path));
            }), "font-add-folder"));
            _folders = PathList("font-folders", MakeFolderRow);
            _folders.bindItem = (row, index) => BindPathRow(row, (string)_folders.itemsSource[index], "folder-path");
            _folderEmpty = Text("添加 Assets 目录以启用界面文字检查。", "fc-list-empty");
            hierarchy.Add(_folders); hierarchy.Add(_folderEmpty);
            hierarchy.Add(Text("递归扫描 .prefab 与 .unity，重叠目录中的文件只检查一次。", "fc-caption"));
            _inputs.Add(hierarchy);

            var rules = Styled(new Foldout { text = "检查规则与范围", value = false }, "fc-rules");
            rules.Add(Text("JSON 自动发现文件名包含 language 的文件，收集全部字符串值，包含 Key、Id 与富文本标记。", "fc-caption"));
            rules.Add(Text("界面扫描读取已保存文字；未保存内容和无法确定的字体、样式切换列为未验证。", "fc-caption"));
            rules.Add(Text("不扫描代码，不推断运行时赋值，不动态补字，也不计入备用字体覆盖。", "fc-caption"));
            _inputs.Add(rules);

            var resultPane = Styled(new VisualElement(), "fc-result-pane"); body.Add(resultPane);
            var resultHeading = Styled(new VisualElement(), "fc-result-heading");
            resultHeading.Add(Text("检查结果", "fc-pane-title"));
            _resultNote = Text("尚未生成结果", "fc-caption fc-result-note"); resultHeading.Add(_resultNote);
            resultPane.Add(resultHeading);
            var resultScroll = Styled(new ScrollView(ScrollViewMode.Vertical), "fc-result-scroll"); resultPane.Add(resultScroll);
            _results = Styled(new VisualElement { name = "font-results" }, "fc-results"); resultScroll.Add(_results);
            AddEmptyState();
            Refresh();
        }

        private static T Styled<T>(T element, string classes) where T : VisualElement
        {
            foreach (var className in classes.Split(' ')) element.AddToClassList(className);
            return element;
        }

        private static Label Text(string text, string classes) => Styled(new Label(text), classes);

        private static Button ActionButton(string text, Action action, string name = "") =>
            Styled(new Button(action) { text = text, name = name }, "fc-button");

        private static VisualElement Section(string title, string description)
        {
            var section = Styled(new VisualElement(), "fc-section");
            section.Add(Text(title, "fc-section-title"));
            section.Add(Text(description, "fc-caption fc-section-caption"));
            return section;
        }

        private static ListView PathList(string name, Func<VisualElement> makeItem) => Styled(new ListView
        {
            name = name, fixedItemHeight = 32, selectionType = SelectionType.None,
            virtualizationMethod = CollectionVirtualizationMethod.FixedHeight, makeItem = makeItem
        }, "fc-path-list");

        internal void Activate()
        {
            _active = true;
            if (!_session.Settings.initialized) RunUi(_session.RefreshSources);
            Refresh();
        }

        private VisualElement MakeSourceRow() => MakePathRow("source-path", guid => _session.RemoveSource(guid));
        private VisualElement MakeFolderRow() => MakePathRow("folder-path", guid => _session.RemoveScanFolder(guid));

        private VisualElement MakePathRow(string name, Action<string> remove)
        {
            var row = Styled(new VisualElement(), "fc-path-row");
            var locate = ActionButton("", () => Locate(AssetDatabase.GUIDToAssetPath((string)row.userData)), name);
            locate.AddToClassList("fc-path"); row.Add(locate);
            var removeButton = ActionButton("移除", () => RunUi(() => remove((string)row.userData)));
            removeButton.AddToClassList("fc-remove"); row.Add(removeButton);
            return row;
        }

        private static void BindPathRow(VisualElement row, string guid, string buttonName)
        {
            row.userData = guid;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var button = row.Q<Button>(buttonName);
            button.text = string.IsNullOrEmpty(path) ? "已移除资源：" + guid : path;
            button.tooltip = button.text;
            button.SetEnabled(!string.IsNullOrEmpty(path));
        }

        internal void Pump() => _session.Pump();
        internal void SynchronizeSettings() { _session.SynchronizeSettings(); Refresh(); }

        private void StateChanged() { Refresh(); _changed(); }

        private void Refresh()
        {
            var settings = _session.Settings;
            var key = JsonUtility.ToJson(settings) + "|" + string.Join("|", settings.sourceGuids.Concat(settings.scanFolderGuids).Select(AssetDatabase.GUIDToAssetPath));
            var font = _active ? _session.TargetFont : null;
            _font.SetValueWithoutNotify(font);
            _font.tooltip = AssetDatabase.GetAssetPath(font);
            _characterFile.SetValueWithoutNotify(AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(settings.characterFileGuid)));
            _characterHint.text = settings.sourceGuids.Contains(settings.characterFileGuid)
                ? "该字符文件也是文案来源，与自身的比较没有独立校验作用。"
                : "仅用于 JSON 文案；界面文字直接检查实际字体。";
            if (_settingsKey != key)
            {
                _settingsKey = key;
                _sources.itemsSource = settings.sourceGuids.ToArray();
                _sources.style.height = Math.Min(settings.sourceGuids.Count, 6) * 32;
                _sources.Rebuild();
                _sourceSection.text = $"来源文件（{settings.sourceGuids.Count}）";
                _sourceSection.tooltip = "移除后刷新不会重新加入；手动添加可恢复。";
                _folders.itemsSource = settings.scanFolderGuids.ToArray();
                _folders.style.height = Math.Min(settings.scanFolderGuids.Count, 6) * 32;
                _folders.Rebuild();
            }
            _sources.style.display = settings.sourceGuids.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _sourceEmpty.style.display = settings.sourceGuids.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _folders.style.display = settings.scanFolderGuids.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _folderEmpty.style.display = settings.scanFolderGuids.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _inputs.SetEnabled(!Running && !_workspace().Running);
            _check.SetEnabled(!Running && !_workspace().Running);
            _cancel.style.display = Running ? DisplayStyle.Flex : DisplayStyle.None;
            _progress.style.display = Running ? DisplayStyle.Flex : DisplayStyle.None;
            _progress.value = _session.Progress;
            _status.text = _session.Status;
            _status.tooltip = _session.Status;
            _resultNote.text = _session.Result == null ? "尚未生成结果" : "检查于 " + _session.Result.CheckedAt;
            var hasFailure = !string.IsNullOrEmpty(_operationFailure ?? _session.Failure);
            var hasResult = _session.Result != null;
            var needsAttention = hasResult && (_session.Result.MissingCharacters.Length > 0 || _session.Result.Unverified.Length > 0);
            _state.text = Running ? "检查中" : _session.Stale ? "结果已过期" : hasFailure ? "检查失败" :
                !hasResult ? "待检查" : needsAttention ? "需要关注" : _session.Result.CharacterCount == 0 ? "无可比对文字" : "覆盖完整";
            _state.EnableInClassList("fc-state-active", Running);
            _state.EnableInClassList("fc-state-warning", !Running && (_session.Stale || needsAttention));
            _state.EnableInClassList("fc-state-error", !Running && hasFailure);
            _state.EnableInClassList("fc-state-success", !Running && !_session.Stale && !hasFailure && hasResult && !needsAttention && _session.Result.CharacterCount > 0);
            _error.text = _operationFailure ?? _session.Failure ?? "";
            _error.style.display = string.IsNullOrEmpty(_error.text) ? DisplayStyle.None : DisplayStyle.Flex;
            if (_shownResult != _session.Result || _shownStale != _session.Stale || _shownRunning != Running)
            {
                _shownResult = _session.Result; _shownStale = _session.Stale; _shownRunning = Running;
                _results.Clear(); _copyMissing = null;
                if (_shownResult != null) AddResult(_shownResult);
                else AddEmptyState();
            }
            _copyMissing?.SetEnabled(!_session.Stale && !Running && _shownResult.MissingCharacters.Length > 0);
        }

        private void AddEmptyState()
        {
            var empty = Styled(new VisualElement(), "fc-empty");
            empty.Add(Text(Running ? "正在核对字符覆盖" : "准备检查字体", "fc-empty-title"));
            empty.Add(Text("配置左侧文案来源或扫描目录，点击“检查字体”。", "fc-empty-description"));
            empty.Add(Text("结果将按字体展示缺字，并提供来源对象定位。", "fc-caption"));
            _results.Add(empty);
        }

        private void AddResult(FontCheckResult result)
        {
            if (_session.Stale) _results.Add(Text("输入已改变，以下结果仅供参考。请重新检查后复制缺失字符。", "fc-notice fc-warning"));
            var missing = result.MissingCharacters;
            var summary = Styled(new VisualElement(), "fc-result-summary");
            var header = Styled(new VisualElement(), "fc-row fc-summary-header");
            var count = Text($"缺失字符  {missing.Length}", "fc-summary-title"); count.name = "font-missing-count";
            header.Add(count);
            _copyMissing = ActionButton("复制缺失字符", () => RunUi(_session.CopyMissing), "font-copy-missing");
            header.Add(_copyMissing); summary.Add(header);
            summary.Add(Text($"检查 {result.SourceCount} 个文件、{result.Fonts.Length} 份字体，已比对 {result.CharacterCount} 个去重字符。", "fc-caption"));
            if (missing.Length > 0) summary.Add(CharacterField("font-missing", missing));
            else summary.Add(Text(result.Unverified.Length > 0 ? "已验证部分未发现缺字，仍有文本未验证。" : result.CharacterCount == 0
                ? "未发现可比对的非空文字。" : "已验证文字在目标字体当前字符表中覆盖完整。", "fc-result-message"));
            var hint = Text($"JSON 字符文件缺失 {result.CharacterFileMissing.Length} 个，字体缺失字符合并 {result.FontMissing.Length} 个。", "fc-caption");
            hint.name = "font-missing-hint"; summary.Add(hint); _results.Add(summary);

            if (result.CharacterFileMissing.Length > 0)
            {
                var characterFile = ResultGroup("JSON 字符文件", $"缺字 {result.CharacterFileMissing.Length}", false, "fc-group-warning");
                characterFile.Add(CharacterField("font-character-file-missing", result.CharacterFileMissing));
                AddSourceDetails(characterFile, result.CharacterFileMissing);
                _results.Add(characterFile);
            }
            foreach (var coverage in result.Fonts)
            {
                var font = ResultGroup(coverage.FontName, $"缺字 {coverage.Missing.Length} / 所需 {coverage.Required.Count}",
                    coverage.Missing.Length > 0, coverage.Missing.Length > 0 ? "fc-group-warning" : "fc-group-clean");
                font.name = "font-coverage";
                var metadata = Styled(new VisualElement(), "fc-row fc-font-metadata");
                var path = Text(coverage.FontPath, "fc-caption fc-font-path"); path.tooltip = coverage.FontPath; metadata.Add(path);
                metadata.Add(ActionButton("定位字体", () => Locate(coverage.FontPath)));
                font.Add(metadata);
                if (coverage.Missing.Length > 0)
                {
                    font.Add(CharacterField("font-coverage-missing", coverage.Missing));
                    AddSourceDetails(font, coverage.Missing);
                }
                else font.Add(Text("当前字符表已覆盖所需文字。", "fc-caption"));
                _results.Add(font);
            }
            if (result.Unverified.Length > 0)
            {
                var unverified = ResultGroup("未验证文本", $"{result.Unverified.Length} 个", true, "fc-group-warning");
                unverified.name = "font-unverified";
                unverified.Add(Text("以下文本不计入覆盖通过。请处理对应原因后重新检查。", "fc-caption"));
                foreach (var source in result.Unverified)
                {
                    var item = Styled(new VisualElement(), "fc-source-item");
                    AddSourceIdentity(item, source);
                    item.Add(Text(source.Limitation, "fc-reason"));
                    AddLocateSource(item, source); unverified.Add(item);
                }
                _results.Add(unverified);
            }
        }

        private static Foldout ResultGroup(string title, string detail, bool expanded, string statusClass) =>
            Styled(new Foldout { text = title + "    " + detail, value = expanded }, "fc-result-group " + statusClass);

        private static TextField CharacterField(string name, FontMissingCharacter[] entries) => Styled(new TextField
        {
            name = name, multiline = true, isReadOnly = true, value = FontCheckResult.CopyText(entries)
        }, "fc-characters");

        private void AddSourceDetails(VisualElement parent, FontMissingCharacter[] missing)
        {
            var entriesBySource = missing.SelectMany(entry => entry.Sources.Select(source => (Source: source, Entry: entry)))
                .GroupBy(pair => pair.Source).OrderBy(group => group.Key.Label, StringComparer.Ordinal).ToArray();
            var fileCount = entriesBySource.Select(group => group.Key.AssetPath).Distinct().Count();
            var sources = Styled(new Foldout { name = "font-missing-sources", text = $"查看来源（{fileCount} 个文件，{entriesBySource.Length} 个位置）", value = false }, "fc-source-foldout");
            foreach (var entry in entriesBySource)
            {
                var characters = entry.Select(pair => pair.Entry).ToArray();
                var source = Styled(new Foldout { text = Path.GetFileName(entry.Key.AssetPath) + $"（{characters.Length} 个缺字）", value = false }, "fc-source-item");
                source.tooltip = entry.Key.Label;
                AddSourceIdentity(source, entry.Key);
                AddLocateSource(source, entry.Key);
                source.Add(CharacterField("font-source-missing", characters));
                sources.Add(source);
            }
            parent.Add(sources);
        }

        private static void AddSourceIdentity(VisualElement parent, FontTextSource source)
        {
            var path = Text(source.AssetPath, "fc-caption fc-source-path"); path.tooltip = source.AssetPath; parent.Add(path);
            if (source.Location != null) parent.Add(Text(source.ObjectPath, "fc-object-path"));
            if (source.LocalizedDefault) parent.Add(Text("本地化组件默认文字", "fc-caption fc-localized"));
        }

        private void AddLocateSource(VisualElement parent, FontTextSource source)
        {
            var actions = Styled(new VisualElement(), "fc-row fc-source-actions");
            var locate = ActionButton(source.Location == null ? "定位资源文件" : "定位对象与组件", () =>
                RunUi(() => AssetCheckLocator.Locate(source.AssetPath, _session.Stale ? null : source.Location)), "font-locate-missing-source");
            locate.tooltip = source.AssetPath;
            actions.Add(locate); parent.Add(actions);
        }

        private static void Locate(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null) { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
        }

        private void RunUi(Action action)
        {
            try { _operationFailure = null; action(); }
            catch (Exception exception) { _operationFailure = exception.Message; JLogger.LogException(exception); }
            Refresh(); _changed();
        }

        public void Dispose() => _session.Dispose();
    }
}
