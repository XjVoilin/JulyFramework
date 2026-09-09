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
        HybridCLRPanel _hybrid;
        ToolsPanel _tools;
        DiffPanel _diff;
        Vector2 _scroll;
        bool _maintenance;
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
            _maintenance = ProjectEditorPrefs.GetBool("BuildTool_Maintenance", false);
            EditorApplication.projectChanged += OnProjectChanged;
        }

        void OnDisable() => EditorApplication.projectChanged -= OnProjectChanged;
        void OnProjectChanged() { _refreshRequested = true; Repaint(); }

        void OnGUI()
        {
            // 配置资产缺失/类型不符是编辑器接入阶段的可恢复状态；展示框架边界错误。
            try { _ctx.SyncConfiguration(); }
            catch (InvalidOperationException exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
                if (GUILayout.Button("刷新配置")) Repaint();
                return;
            }
            if (_platform == null)
            {
                _platform = new PlatformPanel(); _build = new BuildPipelinePanel();
                _hybrid = new HybridCLRPanel(); _tools = new ToolsPanel(); _diff = new DiffPanel();
                foreach (var panel in new IBuildToolPanel[] { _platform, _build, _hybrid, _tools, _diff })
                    panel.OnEnable(_ctx);
            }
            if (_refreshRequested) { _refreshRequested = false; _ctx.RefreshBackups(); }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("构建配置")) Selection.activeObject = _ctx.BuildConfig.Asset;
                if (GUILayout.Button("启动配置")) Selection.activeObject = _ctx.BootConfig.Asset;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _platform.OnGUI();
            _ctx.SyncConfiguration();
            EditorGUILayout.Space(8);
            _build.OnGUI();
            EditorGUILayout.Space(8);
            DrawResult();
            EditorGUILayout.Space(8);
            EditorGUI.BeginChangeCheck();
            _maintenance = EditorGUILayout.Foldout(_maintenance, "维护与诊断", true, EditorStyles.foldoutHeader);
            if (EditorGUI.EndChangeCheck()) ProjectEditorPrefs.SetBool("BuildTool_Maintenance", _maintenance);
            if (_maintenance)
            {
                _hybrid.OnGUI();
                EditorGUILayout.Space(8);
                _tools.OnGUI();
            }
            EditorGUILayout.EndScrollView();
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
