using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    public class BuildToolWindow : EditorWindow
    {
        BuildToolContext _ctx;
        PlatformPanel _platform;
        BuildPipelinePanel _build;
        DiffPanel _diff;
        Vector2 _scroll;
        bool _showConfiguration;
        UnityEditor.Editor _buildEditor;
        UnityEditor.Editor _runtimeEditor;
        bool _refreshRequested;

        [MenuItem("JulyGF/构建/构建工具", priority = 50)]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildToolWindow>("构建工具");
            window.minSize = new Vector2(440, 480);
        }

        void OnEnable()
        {
            _ctx = new BuildToolContext { Repaint = Repaint };
            _platform = null;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            if (_buildEditor != null) DestroyImmediate(_buildEditor);
            if (_runtimeEditor != null) DestroyImmediate(_runtimeEditor);
        }
        void OnProjectChanged() { _refreshRequested = true; Repaint(); }

        void OnGUI()
        {
            BuildConfig config;
            try { config = ReleaseProject.LoadBuildConfig(); }
            catch (InvalidOperationException exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
                if (GUILayout.Button("创建发布配置"))
                    ProjectWindowUtil.CreateAsset(CreateInstance<BuildConfig>(), "BuildConfig.asset");
                return;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawConfiguration(config);
            // 配置不完整时仍允许编辑原资产，不要求先通过 Profile 的校验。
            try { _ctx.SyncConfiguration(); }
            catch (InvalidOperationException exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }
            if (_platform == null)
            {
                _platform = new PlatformPanel(); _build = new BuildPipelinePanel();
                _diff = new DiffPanel();
                foreach (var panel in new IBuildToolPanel[] { _platform, _build, _diff })
                    panel.OnEnable(_ctx);
            }
            if (_refreshRequested) { _refreshRequested = false; _ctx.RefreshBackups(); }

            _platform.OnGUI();
            _ctx.SyncConfiguration();
            EditorGUILayout.Space(8);
            _build.OnGUI();
            EditorGUILayout.Space(8);
            DrawResult();
            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        void DrawConfiguration(BuildConfig config)
        {
            _showConfiguration = EditorGUILayout.Foldout(_showConfiguration, "项目与发布配置", true, EditorStyles.foldoutHeader);
            if (!_showConfiguration)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("发布配置")) Selection.activeObject = config;
                    if (GUILayout.Button("运行配置")) Selection.activeObject = config.bootConfig;
                }
                return;
            }
            UnityEditor.Editor.CreateCachedEditor(config, null, ref _buildEditor);
            _buildEditor.OnInspectorGUI();
            if (config.bootConfig != null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("运行配置（直接编辑原资产）", EditorStyles.boldLabel);
                UnityEditor.Editor.CreateCachedEditor(config.bootConfig, null, ref _runtimeEditor);
                _runtimeEditor.OnInspectorGUI();
            }
        }

        void DrawResult()
        {
            EditorGUILayout.LabelField("结果", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(_ctx.StatusMessage)) EditorGUILayout.HelpBox(_ctx.StatusMessage, _ctx.StatusType);
            if (_ctx.LastBuild == null) { EditorGUILayout.LabelField("尚无本次窗口会话的构建记录", EditorStyles.miniLabel); return; }
            var context = _ctx.LastBuild;
            EditorGUILayout.LabelField("最近构建", $"{context.Platform} · {context.Env} · Core {context.CoreVersion} · Plan {context.PlanVersion}", EditorStyles.wordWrappedLabel);
            if (_ctx.LastResult != null)
                EditorGUILayout.LabelField("耗时", $"{_ctx.LastResult.Elapsed.TotalSeconds:F1} 秒");
            if (!string.IsNullOrEmpty(_ctx.LastPackageVersion))
                EditorGUILayout.LabelField(new GUIContent("资源构建编号", "YooAsset PackageVersion，保持时间戳语义。"), new GUIContent(_ctx.LastPackageVersion));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!string.IsNullOrEmpty(context.CdnOutputDir) && Directory.Exists(context.CdnOutputDir))
                    if (GUILayout.Button("打开资源产物")) EditorUtility.RevealInFinder(context.CdnOutputDir);
                if (context.Artifacts != null && Directory.Exists(context.Artifacts.ExportDirectory))
                    if (GUILayout.Button("打开平台产物")) EditorUtility.RevealInFinder(context.Artifacts.ExportDirectory);
            }
            _diff.OnGUI();
        }
    }
}
