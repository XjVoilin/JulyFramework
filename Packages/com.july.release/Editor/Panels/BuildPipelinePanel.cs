using System.IO;
using July.Release;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    public sealed class BuildPipelinePanel : IBuildToolPanel
    {
        const string PrefKeyStepHybridCLR = "BuildTool_StepHybridCLR";
        const string PrefKeyStepAB = "BuildTool_StepAB";
        const string PrefKeyStepUpload = "BuildTool_StepUpload";
        const string PrefKeyStepMiniGame = "BuildTool_StepMiniGame";
        const string PrefKeyBuildMode = "BuildTool_BuildMode";
        const string PrefKeyQABuild = "BuildTool_QABuild";

        enum BuildMode { Full, HotUpdate }

        static readonly string[] BuildModeLabels = { "全量构建", "热更构建" };

        BuildToolContext _ctx;
        bool _stepHybridCLR, _stepAB, _stepUpload, _stepMiniGame;
        bool _qaBuild;
        BuildMode _buildMode;
        int _backupVersionIndex;

        public void OnEnable(BuildToolContext ctx)
        {
            _ctx = ctx;
            _stepHybridCLR = ProjectEditorPrefs.GetBool(PrefKeyStepHybridCLR, true);
            _stepAB = ProjectEditorPrefs.GetBool(PrefKeyStepAB, true);
            _stepUpload = ProjectEditorPrefs.GetBool(PrefKeyStepUpload, true);
            _stepMiniGame = ProjectEditorPrefs.GetBool(PrefKeyStepMiniGame, false);
            _buildMode = (BuildMode)ProjectEditorPrefs.GetInt(PrefKeyBuildMode, 0);
            _qaBuild = ProjectEditorPrefs.GetBool(PrefKeyQABuild, false);
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("构建流程", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _buildMode = (BuildMode)GUILayout.Toolbar((int)_buildMode, BuildModeLabels);
            if (EditorGUI.EndChangeCheck())
                ProjectEditorPrefs.SetInt(PrefKeyBuildMode, (int)_buildMode);

            EditorGUILayout.Space(4);

            if (_buildMode == BuildMode.Full)
                DrawFullBuildUI();
            else
                DrawHotUpdateBuildUI();
        }

        void DrawFullBuildUI()
        {
            DrawStepToggle(ref _stepHybridCLR, PrefKeyStepHybridCLR, "1. HybridCLR Generate All + 拷贝 DLL");
            DrawStepToggle(ref _stepAB, PrefKeyStepAB, "2. AB 构建 + 拷贝到本地 CDN");
            DrawStepToggle(ref _stepUpload, PrefKeyStepUpload, "3. 上传 CDN 到 COS");
            DrawStepToggle(ref _stepMiniGame, PrefKeyStepMiniGame, "4. 小游戏出包");

            if (_stepAB)
                EditorGUILayout.HelpBox("全量构建完成后将自动备份 AOT 裁剪 DLL，供后续热更构建使用。",
                    MessageType.Info);

            var envName = _ctx.BootConfig != null
                ? _ctx.BootConfig.EnvName
                : "Dev";
            var cdnDir = BuildUtils.GetCdnPath(
                envName, _ctx.CurrentPlatform, PlayerSettings.bundleVersion, _ctx.BuildConfig.planVersion);
            if (_stepAB && Directory.Exists(cdnDir))
                EditorGUILayout.HelpBox("CDN 目录已存在，构建将覆盖现有内容", MessageType.Info);

            EditorGUILayout.Space(4);
            DrawQAToggle();

            EditorGUILayout.Space(8);

            var canBuild = _stepHybridCLR || _stepAB || _stepUpload || _stepMiniGame;
            using (new EditorGUI.DisabledScope(!canBuild))
            {
                var label = _qaBuild ? "▶  执行选中步骤（全量 · QA）" : "▶  执行选中步骤（全量）";
                if (GUILayout.Button(label, GUILayout.Height(32)))
                    ExecuteFullBuild();
            }
        }

        void DrawHotUpdateBuildUI()
        {
            EditorGUILayout.HelpBox(
                "热更构建：仅编译热更 DLL → 检测缺失元数据 → 从 AOT 备份拷贝并瘦身 → 构建 AB → 上传。",
                MessageType.Info);

            var target = EditorUserBuildSettings.activeBuildTarget;
            var versions = HybridCLRBuildHelper.GetAvailableBackupVersions(target, EditorPlatformPref.Platform);

            if (versions.Length == 0)
            {
                EditorGUILayout.HelpBox("未找到 AOT 备份，请先执行一次全量构建。", MessageType.Error);
            }
            else
            {
                _backupVersionIndex = Mathf.Clamp(_backupVersionIndex, 0, versions.Length - 1);
                _backupVersionIndex = EditorGUILayout.Popup("AOT 备份版本", _backupVersionIndex, versions);
            }

            DrawStepToggle(ref _stepAB, PrefKeyStepAB, "构建 AB + 拷贝到本地 CDN");
            DrawStepToggle(ref _stepUpload, PrefKeyStepUpload, "上传 CDN 到 COS");

            EditorGUILayout.Space(4);
            DrawQAToggle();

            EditorGUILayout.Space(8);

            using (new EditorGUI.DisabledScope(versions.Length == 0))
            {
                var label = _qaBuild ? "▶  执行热更构建（QA）" : "▶  执行热更构建";
                if (GUILayout.Button(label, GUILayout.Height(32)))
                    ExecuteHotUpdateBuild(versions[_backupVersionIndex]);
            }
        }

        static void DrawStepToggle(ref bool value, string prefKey, string label)
        {
            EditorGUI.BeginChangeCheck();
            value = EditorGUILayout.ToggleLeft(label, value);
            if (EditorGUI.EndChangeCheck())
                ProjectEditorPrefs.SetBool(prefKey, value);
        }

        void DrawQAToggle()
        {
            EditorGUI.BeginChangeCheck();
            _qaBuild = EditorGUILayout.ToggleLeft(
                $"QA 测试构建（版本覆盖为 {BuildUtils.QAPlanVersion}，不写入 BuildConfig）",
                _qaBuild);
            if (EditorGUI.EndChangeCheck())
                ProjectEditorPrefs.SetBool(PrefKeyQABuild, _qaBuild);

            if (_qaBuild)
                EditorGUILayout.HelpBox(
                    $"CoreVersion 和 PlanVersion 均临时覆盖为 {BuildUtils.QAPlanVersion}。\n" +
                    "不修改 BuildConfig / PlayerSettings，不打 Git Tag。",
                    MessageType.Warning);
        }

        BuildContext CreateBuildContext()
        {
            return new BuildContext
            {
                Target = EditorUserBuildSettings.activeBuildTarget,
                Platform = _ctx.CurrentPlatform,
                Env = _ctx.BootConfig != null
                    ? _ctx.BootConfig.EnvName
                    : "Dev",
                CoreVersion = PlayerSettings.bundleVersion,
                PlanVersion = _ctx.BuildConfig.planVersion,
                Development = _ctx.DebugBuild,
                CloudUrl = _ctx.BuildConfig.cloudUrl,
                CdnUrl = _ctx.BootConfig != null ? _ctx.BootConfig.cdnUrl : null,
            };
        }

        void ExecuteFullBuild()
        {
            var ctx = CreateBuildContext();

            if (_qaBuild)
            {
                ApplyQAOverrides(ctx);
                PlayerSettings.bundleVersion = BuildUtils.QAPlanVersion;
            }
            else
            {
                ctx.CoreVersion = ctx.PlanVersion;
                if (PlayerSettings.bundleVersion != ctx.CoreVersion)
                    PlayerSettings.bundleVersion = ctx.CoreVersion;
            }

            var steps = PipelinePresets.FullBuild(_stepHybridCLR, _stepAB, _stepUpload, _stepMiniGame);
            var result = new July.Build.BuildRunner(new July.Build.UnityBuildHost())
                .Run(ctx, steps);

            if (_qaBuild)
                RestoreBundleVersion(ctx);

            HandleResult(result, ctx);
        }

        void ExecuteHotUpdateBuild(string aotBackupVersion)
        {
            var ctx = CreateBuildContext();
            ctx.AOTBackupVersion = aotBackupVersion;

            if (_qaBuild)
                ApplyQAOverrides(ctx);

            var steps = PipelinePresets.HotUpdate(_stepAB, _stepUpload);
            HandleResult(new July.Build.BuildRunner(new July.Build.UnityBuildHost())
                .Run(ctx, steps), ctx);
        }

        static void ApplyQAOverrides(BuildContext ctx)
        {
            ctx.IsQABuild = true;
            ctx.CoreVersion = BuildUtils.QAPlanVersion;
            ctx.PlanVersion = BuildUtils.QAPlanVersion;
        }

        /// <summary>QA FullBuild 后还原 bundleVersion 为 BuildConfig.planVersion（真实版本）。</summary>
        void RestoreBundleVersion(BuildContext ctx)
        {
            var realVersion = _ctx.BuildConfig != null ? _ctx.BuildConfig.planVersion : "";
            if (!string.IsNullOrEmpty(realVersion) && PlayerSettings.bundleVersion != realVersion)
            {
                PlayerSettings.bundleVersion = realVersion;
                Debug.Log($"[BuildTool] QA 构建完成，bundleVersion 已还原为 {realVersion}");
            }
        }

        void HandleResult(July.Build.BuildResult result, BuildContext ctx)
        {
            if (result.Outcome == July.Build.BuildOutcome.Cancelled) return;

            _ctx.LastPackageVersion = ctx.PackageVersion;

            if (ctx.CdnSnapshotBefore != null && ctx.CdnSnapshotAfter != null)
                _ctx.LastDiff = DiffResult.Compute(ctx.CdnSnapshotBefore, ctx.CdnSnapshotAfter);

            if (result.Succeeded)
            {
                var msg = $"全部完成！平台: {ctx.Platform}  Plan: {ctx.PlanVersion}  " +
                          $"耗时: {result.Elapsed.TotalSeconds:F1}s";
                if (!string.IsNullOrEmpty(ctx.AOTBackupVersion))
                    msg += $"  AOT基线: {ctx.AOTBackupVersion}";

                _ctx.SetStatus(msg, MessageType.Info);

                var pkgInfo = string.IsNullOrEmpty(ctx.PackageVersion) ? "" : $" Pkg:{ctx.PackageVersion}";
                Debug.Log($"[BuildTool] 流程完成 平台:{ctx.Platform} " +
                          $"Core:{ctx.CoreVersion} Plan:{ctx.PlanVersion}{pkgInfo}");
            }
            else
            {
                _ctx.SetStatus($"构建失败: [{result.FailedStep}] {result.Error}", MessageType.Error);
            }
        }

        void RestoreBootConfigEnv()
        {
        }
    }
}
