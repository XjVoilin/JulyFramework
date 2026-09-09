using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// HybridCLR 开发工具面板（默认折叠）。
    /// 提供单独的编译、Generate All、AB 收集器初始化等快捷操作。
    /// </summary>
    public sealed class HybridCLRPanel : IBuildToolPanel
    {
        const string PrefKeyExpanded = "BuildTool_HybridCLRExpanded";

        BuildToolContext _ctx;
        bool _expanded;

        public void OnEnable(BuildToolContext ctx)
        {
            _ctx = ctx;
            _expanded = ProjectEditorPrefs.GetBool(PrefKeyExpanded, false);
        }

        public void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            _expanded = EditorGUILayout.Foldout(_expanded, "HybridCLR 工具", true, EditorStyles.foldoutHeader);
            if (EditorGUI.EndChangeCheck())
                ProjectEditorPrefs.SetBool(PrefKeyExpanded, _expanded);

            if (!_expanded) return;

            EditorGUI.indentLevel++;

            var target = EditorUserBuildSettings.activeBuildTarget;

            // ── 常用 ──
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("编译热更 DLL 并拷贝"))
                RunWithProgressBar(() =>
                    HybridCLRBuildHelper.CompileAndCopyDlls(target, _ctx.DebugBuild));

            if (GUILayout.Button("Generate All"))
            {
                var ok = HybridCLRBuildHelper.GenerateAll();
                _ctx.SetStatus(
                    ok ? "Generate All 完成，请检查 AOTGenericReferences.cs" : "Generate All 失败，请查看 Console",
                    ok ? MessageType.Info : MessageType.Error);
            }

            EditorGUILayout.EndHorizontal();

            // ── 热更 ──
            var platform = EditorPlatformPref.Platform;
            var versions = HybridCLRBuildHelper.GetAvailableBackupVersions(target, platform);
            using (new EditorGUI.DisabledScope(versions.Length == 0))
            {
                var label = versions.Length > 0
                    ? $"热更编译（使用备份 {versions[0]}·{platform}）"
                    : $"热更编译（无 AOT 备份·{platform}）";

                if (GUILayout.Button(label))
                    RunWithProgressBar(() =>
                        HybridCLRBuildHelper.CompileHotUpdateOnly(target, platform, versions[0], _ctx.DebugBuild));
            }

            // ── 初始化 ──
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("初始化", EditorStyles.miniLabel);

            if (GUILayout.Button("初始化 AB 收集器（HotFix + AOTMeta）"))
            {
                HybridCLRBuildHelper.EnsureABCollector();
                var ok = HybridCLRBuildHelper.HasRequiredABGroups();
                _ctx.SetStatus(
                    ok ? "HotFix + AOTMeta AB 分组已就绪" : "AB 分组配置失败",
                    ok ? MessageType.Info : MessageType.Error);
            }

            // ── 状态 ──
            EditorGUILayout.Space(4);
            var settingsOk = HybridCLRBuildHelper.ValidateSettings(logErrors: false);
            var abGroupOk = HybridCLRBuildHelper.HasRequiredABGroups();

            EditorGUILayout.LabelField("配置状态",
                $"Settings: {(settingsOk ? "✓" : "✗")}   AB 分组: {(abGroupOk ? "✓" : "✗")}",
                EditorStyles.miniLabel);

            if (versions.Length > 0)
                EditorGUILayout.LabelField("AOT 备份",
                    $"{versions.Length} 个版本，最新: {versions[0]}",
                    EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField("AOT 备份", "无（需先全量构建）", EditorStyles.miniLabel);

            EditorGUI.indentLevel--;
        }

        void RunWithProgressBar(System.Func<bool> action)
        {
            try
            {
                var ok = action();
                _ctx.SetStatus(
                    ok ? "操作完成" : "操作失败，请查看 Console",
                    ok ? MessageType.Info : MessageType.Error);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
