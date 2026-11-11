using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace July.Font.Authoring
{
    public sealed class FontAuthoringWindow : EditorWindow
    {
        private FontToolSession _session;
        private FontAuthoringSettings _settings;
        private Vector2 _scroll;
        private bool _missingOpen, _pendingSubset;
        private string _uiError = "";

        [MenuItem("JulyGF/资源管理/字体裁剪")]
        public static void ShowWindow()
        {
            var window = GetWindow<FontAuthoringWindow>("字体裁剪");
            window.minSize = new Vector2(440, 500);
        }

        private void OnEnable()
        {
            _settings = FontAuthoringSettings.instance;
            var package = PackageInfo.FindForAssembly(typeof(FontAuthoringWindow).Assembly);
            _session = new FontToolSession(package.resolvedPath, Path.GetFullPath("."));
            _session.Changed += Repaint;
            _session.Completed += OnCompleted;
            Execute(_session.DetectEnvironment);
        }

        private void OnDisable()
        {
            _pendingSubset = false;
            _session.Completed -= OnCompleted;
            _session.Changed -= Repaint;
            _session.Dispose();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 88;
            try
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    GUILayout.Space(8);
                    using (new EditorGUI.DisabledScope(_session.Busy))
                    {
                        EditorGUI.BeginChangeCheck();
                        DrawInputs();
                        if (EditorGUI.EndChangeCheck())
                        {
                            _settings.Save(); _session.InvalidateResult(); _pendingSubset = false; _uiError = "";
                        }
                    }
                    GUILayout.Space(12);
                    DrawActions();
                    GUILayout.Space(12);
                    DrawResult();
                    GUILayout.Space(12);
                    DrawEnvironment();
                    var error = string.IsNullOrEmpty(_uiError) ? _session.Failure : _uiError;
                    if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = labelWidth;
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawInputs()
        {
            EditorGUILayout.LabelField("字体与输出", EditorStyles.boldLabel);
            _settings.source = DrawPath("源字体", _settings.source, true);
            EditorGUILayout.HelpBox("输出到原字体同级目录，文件名追加 -subset，保留源字体格式。", MessageType.None);
            GUILayout.Space(10);
            EditorGUILayout.LabelField("保留字符", EditorStyles.boldLabel);
            _settings.useDefault = EditorGUILayout.Popup("基础字符集", _settings.useDefault ? 0 : 1,
                new[] { "简体中文常用字符（默认）", "仅使用补充字符" }) == 0;
            if (_settings.useDefault)
                EditorGUILayout.LabelField("GB2312 汉字、ASCII、常用标点与符号、全角字符。", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("额外字符");
                GUILayout.Label("去重后 " + ExtraCharacterCount(_settings.extraText) + " 个字符", EditorStyles.miniLabel);
            }
            _settings.extraText = EditorGUILayout.TextArea(_settings.extraText, GUILayout.MinHeight(66));
            _settings.characterFile = DrawPath("字符文件", _settings.characterFile, false);
            EditorGUILayout.LabelField("可选：UTF-8 .txt 文件；与基础字符集、额外字符合并去重。", EditorStyles.wordWrappedMiniLabel);
        }

        private static string DrawPath(string label, string path, bool font)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                path = EditorGUILayout.TextField(label, path);
                var dropArea = GUILayoutUtility.GetLastRect();
                var current = Event.current;
                if (GUI.enabled && dropArea.Contains(current.mousePosition) &&
                    (current.type == EventType.DragUpdated || current.type == EventType.DragPerform))
                {
                    var dropped = DragAndDrop.paths.FirstOrDefault();
                    if (dropped != null)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        if (current.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); path = dropped; GUI.changed = true; }
                        current.Use();
                    }
                }
                if (GUILayout.Button("浏览…", GUILayout.Width(60)))
                {
                    var selected = EditorUtility.OpenFilePanelWithFilters(font ? "选择源字体" : "选择字符文件", "",
                        font ? new[] { "字体文件", "ttf,otf" } : new[] { "UTF-8 字符文件", "txt" });
                    if (!string.IsNullOrEmpty(selected)) { path = ProjectPath(selected); GUI.changed = true; }
                }
            }
            return path;
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField(_session.Status, EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (_session.Busy)
                {
                    if (GUILayout.Button("取消", GUILayout.Height(26)))
                        Execute(() => { _pendingSubset = false; _session.Cancel(); });
                }
                else
                {
                    using (new EditorGUI.DisabledScope(!_session.Ready || string.IsNullOrWhiteSpace(_settings.source)))
                    {
                        if (GUILayout.Button("预检查", GUILayout.Height(26))) Execute(() => Begin(false));
                        if (GUILayout.Button("开始裁剪", GUILayout.Height(26))) Execute(() => Begin(true));
                    }
                }
            }
        }

        private FontSubsetRequest Request() => new()
        {
            input = _settings.source, useDefault = _settings.useDefault,
            extraText = _settings.extraText, characterFile = _settings.characterFile
        };

        private void Begin(bool subset)
        {
            _pendingSubset = subset;
            // Recheck current files on every run; a prior check is only a display snapshot.
            _session.Start(Request(), false);
        }

        private void OnCompleted(FontSubsetResult result)
        {
            Execute(() =>
            {
                if (result.written)
                {
                    ImportOutput(result.output); ImportOutput(result.charsetOutput);
                    EditorUtility.RevealInFinder(result.output);
                    return;
                }
                if (!_pendingSubset) return;
                _pendingSubset = false;
                if (result.missing.Length > 0 && !EditorUtility.DisplayDialog("原字体缺字",
                    $"请求 {result.requested} 个字符，原字体缺少 {result.missing.Length} 个。\n" +
                    string.Join("、", result.missing.Take(12)) + "\n\n继续只会保留原字体已有字符，无法补出缺字。", "继续裁剪", "取消")) return;
                var exists = File.Exists(result.output) || File.Exists(result.charsetOutput);
                if (exists && !EditorUtility.DisplayDialog("替换已有裁剪文件",
                    "将替换以下输出，原字体保持不变：\n" + result.output + "\n" + result.charsetOutput, "替换", "取消")) return;
                var request = Request();
                request.allowMissing = result.missing.Length > 0;
                request.fingerprint = result.fingerprint;
                request.replace = exists;
                _session.Start(request, true);
            });
        }

        private void DrawResult()
        {
            var result = _session.Result;
            if (result == null) return;
            EditorGUILayout.LabelField(result.written ? "裁剪结果" : "预检查结果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"请求 {result.requested} 个字符 · 原字体包含 {result.covered} 个 · 原字体缺字 {result.missing.Length} 个", EditorStyles.wordWrappedLabel);
            if (result.written)
            {
                EditorGUILayout.LabelField($"{result.originalBytes / 1048576.0:F2} MB → {result.outputBytes / 1048576.0:F2} MB" +
                    $"（体积变化 {(result.outputBytes / (double)result.originalBytes - 1) * 100:+0.0;-0.0;0}%）");
                EditorGUILayout.LabelField("裁剪丢字：0（以原字体已有字符为准）", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(result.output, EditorStyles.wordWrappedMiniLabel);
            }
            if (result.missing.Length > 0)
            {
                _missingOpen = EditorGUILayout.Foldout(_missingOpen, "原字体缺失字符", true);
                if (_missingOpen)
                {
                    EditorGUILayout.SelectableLabel(string.Join("、", result.missing.Take(40)) +
                        (result.missing.Length > 40 ? " …" : ""), EditorStyles.wordWrappedLabel, GUILayout.Height(75));
                    if (GUILayout.Button("复制全部缺失字符")) EditorGUIUtility.systemCopyBuffer = result.missingText;
                }
            }
        }

        private void DrawEnvironment()
        {
            EditorGUILayout.LabelField("工具环境", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Python", _session.HasPython ? _session.PythonVersion : _session.Busy ? "正在自动检测…" : "未检测到可用解释器");
            if (_session.HasPython)
                EditorGUILayout.SelectableLabel(_session.PythonPath, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(32));
            EditorGUILayout.LabelField("fontTools", _session.Ready ? "已就绪" : "待初始化或检查");
            using (new EditorGUI.DisabledScope(_session.Busy))
            {
                EditorGUILayout.LabelField("依赖仅安装到 Library/July/FontAuthoring，不修改系统依赖。", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("重新检测")) Execute(_session.DetectEnvironment);
                    using (new EditorGUI.DisabledScope(!_session.HasPython))
                        if (GUILayout.Button("初始化工具环境")) Execute(_session.InitializeEnvironment);
                }
            }
        }

        internal static int ExtraCharacterCount(string text)
        {
            var points = new System.Collections.Generic.HashSet<int>();
            for (var index = 0; index < text.Length; index++)
            {
                var point = char.ConvertToUtf32(text, index);
                if (CharUnicodeInfo.GetUnicodeCategory(text, index) != UnicodeCategory.Control && point != 0xFEFF)
                    points.Add(point);
                if (char.IsHighSurrogate(text[index])) index++;
            }
            return points.Count;
        }

        private static string ProjectPath(string path)
        {
            var full = Path.GetFullPath(path);
            var assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            return full.StartsWith(assets, StringComparison.OrdinalIgnoreCase) ? "Assets/" + full.Substring(assets.Length).Replace('\\', '/') : full;
        }

        private static void ImportOutput(string path)
        {
            var projectPath = ProjectPath(path);
            if (projectPath.StartsWith("Assets/", StringComparison.Ordinal)) AssetDatabase.ImportAsset(projectPath);
        }

        private void Execute(Action action)
        {
            _uiError = "";
            try { action(); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                exception is ArgumentException || exception is InvalidOperationException || exception is Win32Exception)
            {
                _pendingSubset = false; _uiError = exception.Message;
                Debug.LogException(exception);
            }
        }
    }
}
