using System;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    public sealed class HybridCLRPanel : IBuildToolPanel
    {
        BuildToolContext _ctx;
        string _hash;
        string _hashScope;
        string _settingsStatus;
        public void OnEnable(BuildToolContext context) => _ctx = context;

        public void OnGUI()
        {
            EditorGUILayout.LabelField("HybridCLR", EditorStyles.boldLabel);
            var target = EditorUserBuildSettings.activeBuildTarget;
            var scope = _ctx.CurrentPlatform + "|" + target + "|" + _ctx.DebugBuild;
            if (_hashScope != scope) { _hashScope = scope; _hash = null; _settingsStatus = null; }
            // 主构建区和维护区共用所选基线，不另外默认选取最新备份。
            if (_ctx.Selection.Mode != ReleaseBuildMode.HotUpdate) BuildPipelinePanel.DrawBaseline(_ctx);
            else EditorGUILayout.LabelField("当前热更基线", _ctx.Selection.AotBaseline ?? "无");
            var ready = new PlatformDefinesValidationStep().Validate(_ctx.CreatePreview()) == null;
            using (new EditorGUI.DisabledScope(!ready))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("编译 DLL 并拷贝")) Run(() => HybridCLRBuildHelper.CompileAndCopyDlls(target, _ctx.DebugBuild));
                    if (GUILayout.Button("Generate All")) Run(HybridCLRBuildHelper.GenerateAll);
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_ctx.Selection.AotBaseline)))
                    if (GUILayout.Button("按所选基线编译热更 DLL"))
                        Run(() => HybridCLRBuildHelper.CompileHotUpdateOnly(target, _ctx.CurrentPlatform, _ctx.Selection.AotBaseline, _ctx.DebugBuild));
            }
            if (!ready) EditorGUILayout.HelpBox("请先应用目标平台与 Debug 设置，再执行编译工具。", MessageType.Info);
            if (GUILayout.Button("初始化 DLL 资源收集器"))
            {
                HybridCLRBuildHelper.EnsureABCollector();
                CheckSettings();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("检查配置")) CheckSettings();
                if (GUILayout.Button("计算当前 AOT Hash")) _hash = AotSourceHasher.ComputeHash();
            }
            if (_settingsStatus != null) EditorGUILayout.LabelField(_settingsStatus, EditorStyles.wordWrappedLabel);
            if (_hash != null)
            {
                EditorGUILayout.SelectableLabel(_hash, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField("此次计算结果；修改源码后需重新计算。热更执行时仍会自动检查 AOT Hash。", EditorStyles.wordWrappedMiniLabel);
            }
        }

        void CheckSettings()
        {
            var valid = HybridCLRBuildHelper.ValidateSettings();
            _settingsStatus = valid ? "HybridCLR 与 DLL 收集器配置通过" : "配置未通过，请查看 Console";
            _ctx.SetStatus(_settingsStatus, valid ? MessageType.Info : MessageType.Error);
        }

        void Run(Func<bool> action)
        {
            try
            {
                var ok = action();
                _ctx.SetStatus(ok ? "操作完成" : "操作失败，请查看 Console", ok ? MessageType.Info : MessageType.Error);
            }
            finally { EditorUtility.ClearProgressBar(); _ctx.RefreshBackups(); }
        }
    }
}
