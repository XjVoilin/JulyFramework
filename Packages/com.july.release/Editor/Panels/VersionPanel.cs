using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    public sealed class VersionPanel : IBuildToolPanel
    {
        BuildToolContext _ctx;
        string _versionError;
        string _liveVersion;
        string _queryScope;
        bool _queried;
        public void OnEnable(BuildToolContext context) => _ctx = context;

        public void OnGUI()
        {
            using (new EditorGUI.DisabledScope(_ctx.Selection.QA))
            {
                EditorGUI.BeginChangeCheck();
                var version = EditorGUILayout.DelayedTextField(
                    new GUIContent("资源版本", "PlanVersion。全量构建的主包版本同步为此值；热更沿用所选基线。"),
                    _ctx.Selection.QA ? BuildUtils.QAPlanVersion : _ctx.BuildConfig.planVersion);
                if (EditorGUI.EndChangeCheck())
                {
                    version = version.Trim();
                    _versionError = BuildUtils.IsValidVersion(version) ? null : "版本格式需要为 x.y.z";
                    if (_versionError == null)
                    {
                        Undo.RecordObject(_ctx.BuildConfig.Asset, "修改资源版本");
                        _ctx.BuildConfig.planVersion = version;
                        EditorUtility.SetDirty(_ctx.BuildConfig.Asset);
                        AssetDatabase.SaveAssetIfDirty(_ctx.BuildConfig.Asset);
                    }
                }
            }
            if (_versionError != null && !_ctx.Selection.QA)
                EditorGUILayout.HelpBox(_versionError, MessageType.Error);

            var preview = _ctx.CreatePreview();
            EditorGUILayout.LabelField("本次主包版本", preview.CoreVersion ?? "请选择热更基线");
            var scope = _ctx.BootConfig.GetConfigServerUrl() + "|" + preview.Platform + "|" + preview.CoreVersion;
            if (_queryScope != scope) { _queryScope = scope; _queried = false; _liveVersion = null; }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("线上资源版本", _ctx.Selection.QA
                    ? "QA 固定版本，可重复覆盖"
                    : _queried ? _liveVersion ?? "查询失败或尚未配置" : "未查询");
                using (new EditorGUI.DisabledScope(_ctx.Selection.QA || !BuildUtils.IsValidVersion(preview.CoreVersion)))
                    if (GUILayout.Button("查询", GUILayout.Width(60)))
                    {
                        _liveVersion = EditorConfigService.FetchLivePlanVersion(
                            _ctx.BootConfig.GetConfigServerUrl(), preview.Platform, preview.CoreVersion);
                        _queried = true;
                    }
            }
        }
    }
}
