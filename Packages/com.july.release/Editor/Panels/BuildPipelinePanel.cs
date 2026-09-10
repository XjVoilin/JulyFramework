using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    public sealed class BuildPipelinePanel : IBuildToolPanel
    {
        readonly VersionPanel _versions = new();
        BuildToolContext _ctx;
        BuildToolSelection Selection => _ctx.Selection;

        public void OnEnable(BuildToolContext ctx)
        {
            _ctx = ctx;
            Selection.Mode = (ReleaseBuildMode)ProjectEditorPrefs.GetInt("BuildTool_BuildMode", 0);
            Selection.QA = ProjectEditorPrefs.GetBool("BuildTool_QABuild", false);
            Selection.Upload = ProjectEditorPrefs.GetBool("BuildTool_StepUpload", false);
            Selection.CustomSteps = ProjectEditorPrefs.GetBool("BuildTool_CustomSteps", false);
            Selection.HybridCLR = ProjectEditorPrefs.GetBool("BuildTool_StepHybridCLR", true);
            Selection.AssetBundles = ProjectEditorPrefs.GetBool("BuildTool_StepAB", true);
            Selection.MiniGame = ProjectEditorPrefs.GetBool("BuildTool_StepMiniGame", true);
            _versions.OnEnable(ctx);
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("构建", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Selection.Mode = (ReleaseBuildMode)GUILayout.Toolbar((int)Selection.Mode, new[] { "全量构建", "热更构建" });
            Selection.QA = EditorGUILayout.ToggleLeft(
                new GUIContent("QA 测试版本（99.99.99）", "构建期间临时使用测试版本，结束后恢复原主包版本；不修改资源版本配置，不打 Git Tag。"), Selection.QA);
            Selection.Upload = EditorGUILayout.ToggleLeft("上传本次资源到 CDN（COS）", Selection.Upload);
            Selection.CustomSteps = EditorGUILayout.ToggleLeft("自定义构建步骤", Selection.CustomSteps);
            if (Selection.CustomSteps)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (Selection.Mode == ReleaseBuildMode.Full)
                        Selection.HybridCLR = EditorGUILayout.ToggleLeft("HybridCLR Generate All + 拷贝 DLL", Selection.HybridCLR);
                    Selection.AssetBundles = EditorGUILayout.ToggleLeft("构建 AB + 拷贝本地 CDN", Selection.AssetBundles);
                    if (Selection.Mode == ReleaseBuildMode.Full)
                        Selection.MiniGame = EditorGUILayout.ToggleLeft("平台出包 + 预下载注入", Selection.MiniGame);
                }
            }
            if (EditorGUI.EndChangeCheck()) SaveSelection();

            if (Selection.Mode == ReleaseBuildMode.HotUpdate) DrawBaseline(_ctx);
            _versions.OnGUI();
            var preview = _ctx.Preview;
            var error = preview.Validate();
            if (Selection.Mode == ReleaseBuildMode.HotUpdate && string.IsNullOrEmpty(Selection.AotBaseline))
                error = "没有可用 AOT 基线，请先执行全量构建。";
            if (Selection.Mode == ReleaseBuildMode.HotUpdate && Selection.Upload && !Selection.BuildBundles)
                error = "热更上传需要重新生成 preload.json，请启用 AB 构建。";
            if (error == null) error = new PlatformDefinesValidationStep().Validate(preview);
            var hasWork = Selection.Mode == ReleaseBuildMode.HotUpdate || !Selection.CustomSteps ||
                          Selection.HybridCLR || Selection.AssetBundles || Selection.MiniGame || Selection.Upload;
            if (!hasWork) error = "请至少选择一个构建步骤。";

            if (BuildUtils.IsValidVersion(preview.CoreVersion) && BuildUtils.IsValidVersion(preview.PlanVersion))
            {
                var local = BuildUtils.GetCdnPath(preview.Env, preview.Platform, preview.CoreVersion, preview.PlanVersion);
                EditorGUILayout.LabelField("本地资源", local, EditorStyles.wordWrappedLabel);
                if (Selection.ExportPlayer)
                    EditorGUILayout.LabelField("平台产物", PlatformBuildPaths.GetExportDirectory(preview), EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("远端上传", DescribeUploads(preview), EditorStyles.wordWrappedLabel);
                if (Selection.Upload)
                    EditorGUILayout.LabelField("COS 项目根", preview.CloudUrl, EditorStyles.wordWrappedLabel);
                if (Selection.BuildBundles && !Selection.QA && Directory.Exists(local))
                    EditorGUILayout.HelpBox("本次资源目录已存在，构建会覆盖其中的同名文件。", MessageType.Info);
            }
            if (GUILayout.Button("检查本次构建接入"))
            {
                var errors = ReleaseConfigurationCheck.Inspect(ReleaseProject.LoadBuildConfig(), preview,
                    Selection.BuildBundles, Selection.Mode == ReleaseBuildMode.HotUpdate || !Selection.CustomSteps || Selection.HybridCLR,
                    Selection.ExportPlayer, Selection.Upload);
                var defineError = new PlatformDefinesValidationStep().Validate(preview);
                var report = string.Join("\n", errors);
                if (defineError != null) report += "\n" + defineError;
                _ctx.SetStatus(report.Length == 0 ? "接入检查通过（未编译、未联网、未上传）。" : report.Trim(),
                    report.Length == 0 ? MessageType.Info : MessageType.Error);
            }
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            using (new EditorGUI.DisabledScope(error != null))
                if (GUILayout.Button(Selection.CustomSteps ? "执行所选步骤" : "开始构建", GUILayout.Height(32)))
                    Execute(preview);
        }

        internal static void DrawBaseline(BuildToolContext ctx)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (ctx.BackupVersions.Length == 0)
                    EditorGUILayout.LabelField("热更基线", "无 AOT 备份");
                else
                {
                    var index = Array.IndexOf(ctx.BackupVersions, ctx.Selection.AotBaseline);
                    EditorGUI.BeginChangeCheck();
                    var selected = EditorGUILayout.Popup("热更基线", index, ctx.BackupVersions);
                    if (EditorGUI.EndChangeCheck()) ctx.SelectBaseline(ctx.BackupVersions[selected]);
                }
                if (GUILayout.Button("刷新", GUILayout.Width(60))) ctx.RefreshBackups();
            }
        }

        string DescribeUploads(BuildContext context)
        {
            if (!Selection.Upload) return "关闭（仅生成本地文件）";
            if (Selection.Mode == ReleaseBuildMode.HotUpdate) return "AB、preload.json";
            if (!Selection.ExportPlayer) return "AB";
            return context.Platform == July.Release.PlatformKeys.WeChat
                ? "AB、微信首包 data、本次生成的 preload.json" : "AB、本次生成的 preload.json";
        }

        void Execute(BuildContext context)
        {
            var originalVersion = PlayerSettings.bundleVersion;
            _ctx.LastDiff = null;
            _ctx.LastPackageVersion = null;
            _ctx.LastResult = null;
            _ctx.LastBuild = context;
            try
            {
                if (Selection.Mode == ReleaseBuildMode.Full)
                    PlayerSettings.bundleVersion = context.CoreVersion;
                var result = new July.Build.BuildRunner(new July.Build.UnityBuildHost()).Run(context, Selection.CreateSteps());
                _ctx.LastResult = result;
                _ctx.LastPackageVersion = context.PackageVersion;
                if (context.CdnSnapshotBefore != null && context.CdnSnapshotAfter != null)
                    _ctx.LastDiff = DiffResult.Compute(context.CdnSnapshotBefore, context.CdnSnapshotAfter);
                var message = result.Succeeded ? "构建完成" : result.Outcome == July.Build.BuildOutcome.Cancelled
                    ? "构建已取消" : $"构建失败：[{result.FailedStep}] {result.Error}";
                _ctx.SetStatus(message, result.Succeeded ? MessageType.Info :
                    result.Outcome == July.Build.BuildOutcome.Cancelled ? MessageType.Warning : MessageType.Error);
            }
            finally
            {
                if (context.IsQABuild) PlayerSettings.bundleVersion = originalVersion;
                _ctx.RefreshBackups();
            }
        }

        void SaveSelection()
        {
            ProjectEditorPrefs.SetInt("BuildTool_BuildMode", (int)Selection.Mode);
            ProjectEditorPrefs.SetBool("BuildTool_QABuild", Selection.QA);
            ProjectEditorPrefs.SetBool("BuildTool_StepUpload", Selection.Upload);
            ProjectEditorPrefs.SetBool("BuildTool_CustomSteps", Selection.CustomSteps);
            ProjectEditorPrefs.SetBool("BuildTool_StepHybridCLR", Selection.HybridCLR);
            ProjectEditorPrefs.SetBool("BuildTool_StepAB", Selection.AssetBundles);
            ProjectEditorPrefs.SetBool("BuildTool_StepMiniGame", Selection.MiniGame);
        }
    }
}
