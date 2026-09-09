using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    /// <summary>
    /// 版本信息面板（见 spec §2.4 / §7.1）。
    /// <para>
    /// - PlanVersion 可在本地编辑（写回 BuildConfig.planVersion），供本地打包/调试使用。
    /// - CoreVersion = PlayerSettings.bundleVersion 保持只读；本地 FullBuild 会自动同步为 PlanVersion。
    /// - Jenkins 构建会用 -planVersion 参数覆盖本地值（workspace 本地修改不 commit）。
    /// </para>
    /// </summary>
    public sealed class VersionPanel : IBuildToolPanel
    {
        BuildToolContext _ctx;
        string _planVersionEdit;
        string _planVersionError;
        string _liveVersion;
        bool _liveVersionQueried;
        string[] _backupVersions;
        string _currentAotHash;
        bool _hashComputed;

        public void OnEnable(BuildToolContext ctx)
        {
            _ctx = ctx;
            _planVersionEdit = _ctx.BuildConfig != null ? _ctx.BuildConfig.planVersion : "";
            RefreshBackupList();
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("版本信息", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "PlanVersion 可本地编辑，供本地 BuildTool 打包使用。\n" +
                "Jenkins 构建会用 -planVersion 参数覆盖（workspace 修改不 commit 回 git）。\n" +
                "CoreVersion = PlayerSettings.bundleVersion 保持只读；本地 FullBuild 会自动同步为 PlanVersion。",
                MessageType.Info);

            DrawPlanVersionEditor();

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Core Version (bundleVersion)", PlayerSettings.bundleVersion);

            EditorGUILayout.Space(4);
            DrawLiveVersion();

            EditorGUILayout.Space(4);
            if (!string.IsNullOrEmpty(_ctx.LastPackageVersion))
                EditorGUILayout.LabelField("上次构建 YooAsset PackageVersion", _ctx.LastPackageVersion);

            EditorGUILayout.Space(8);
            DrawBackupList();

            EditorGUILayout.Space(8);
            DrawAotHashSection();
        }

        void DrawPlanVersionEditor()
        {
            if (_ctx.BuildConfig == null)
            {
                EditorGUILayout.HelpBox("BuildConfig 资产丢失，无法编辑 PlanVersion。", MessageType.Error);
                return;
            }

            EditorGUI.BeginChangeCheck();
            // DelayedTextField：失焦或回车时才触发，避免每帧校验
            _planVersionEdit = EditorGUILayout.DelayedTextField("Plan Version", _planVersionEdit);
            if (EditorGUI.EndChangeCheck())
                TryApplyPlanVersion(_planVersionEdit);

            if (!string.IsNullOrEmpty(_planVersionError))
                EditorGUILayout.HelpBox(_planVersionError, MessageType.Error);
        }

        void TryApplyPlanVersion(string value)
        {
            var trimmed = (value ?? "").Trim();
            if (!BuildUtils.IsValidVersion(trimmed))
            {
                _planVersionError = $"格式非法: \"{trimmed}\"，需要 x.y.z";
                return;
            }

            _planVersionError = null;
            if (_ctx.BuildConfig.planVersion == trimmed) return;
            _ctx.BuildConfig.planVersion = trimmed;
            EditorUtility.SetDirty(_ctx.BuildConfig.Asset);
            AssetDatabase.SaveAssets();
        }

        void DrawLiveVersion()
        {
            EditorGUILayout.BeginHorizontal();

            var label = _liveVersionQueried ? (_liveVersion ?? "查询失败") : "未查询";
            EditorGUILayout.LabelField("线上 Plan Version", label);

            if (GUILayout.Button("刷新", GUILayout.Width(60)))
            {
                var configServerUrl = _ctx.BootConfig != null
                    ? _ctx.BootConfig.GetConfigServerUrl()
                    : null;
                _liveVersion = EditorConfigService.FetchLivePlanVersion(
                    configServerUrl, _ctx.CurrentPlatform);
                _liveVersionQueried = true;
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawBackupList()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("已归档 AOT 备份（降序）", EditorStyles.boldLabel);
            if (GUILayout.Button("刷新", GUILayout.Width(60)))
                RefreshBackupList();
            EditorGUILayout.EndHorizontal();

            if (_backupVersions == null || _backupVersions.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "未找到 AOT 备份。FullBuild 首次跑之后会产出。\n" +
                    "查询路径：workspace(HybridCLRData/AOTBackup) 与持久目录(../AOTBackup/项目名/)",
                    MessageType.Warning);
                return;
            }

            foreach (var v in _backupVersions)
                EditorGUILayout.LabelField("  • " + v);
        }

        void DrawAotHashSection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ScriptsAot 当前 hash", EditorStyles.boldLabel);
            if (GUILayout.Button("重新计算", GUILayout.Width(80)))
            {
                _currentAotHash = AotSourceHasher.ComputeHash();
                _hashComputed = true;
            }
            EditorGUILayout.EndHorizontal();

            if (!_hashComputed)
            {
                EditorGUILayout.LabelField("  点「重新计算」查看");
                return;
            }

            EditorGUILayout.SelectableLabel(
                string.IsNullOrEmpty(_currentAotHash) ? "<empty>" : _currentAotHash,
                EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            EditorGUILayout.HelpBox(
                "HotUpdate 会把此 hash 和目标 AOT 备份里的 aot-source.hash 对比。" +
                "若修改了 ScriptsAot/ 下任意 .cs/.asmdef 文件（包括注释/空白），hash 就会变化，必须走 FullBuild。",
                MessageType.None);
        }

        void RefreshBackupList()
        {
            _backupVersions = HybridCLRBuildHelper.GetAvailableBackupVersions(
                EditorUserBuildSettings.activeBuildTarget, EditorPlatformPref.Platform);
        }
    }
}
