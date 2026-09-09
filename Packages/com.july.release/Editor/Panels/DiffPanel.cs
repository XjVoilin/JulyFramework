using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    public sealed class DiffPanel : IBuildToolPanel
    {
        static GUIStyle _richLabel;
        static GUIStyle _richLabelWrap;

        static GUIStyle RichLabel =>
            _richLabel ??= new GUIStyle(EditorStyles.label) { richText = true, wordWrap = false };

        static GUIStyle RichLabelWrap =>
            _richLabelWrap ??= new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };

        BuildToolContext _ctx;
        bool _showDiff;
        Vector2 _scrollPos;

        public void OnEnable(BuildToolContext ctx) => _ctx = ctx;

        public void OnGUI()
        {
            if (_ctx.LastDiff == null) return;

            _showDiff = EditorGUILayout.Foldout(_showDiff, "构建差异", true, EditorStyles.foldoutHeader);
            if (!_showDiff) return;

            var diff = _ctx.LastDiff;

            var addedCount = diff.Entries.Count(e => e.Type == DiffResult.DiffType.Added);
            var modifiedCount = diff.Entries.Count(e => e.Type == DiffResult.DiffType.Modified);
            var removedCount = diff.Entries.Count(e => e.Type == DiffResult.DiffType.Removed);

            EditorGUILayout.LabelField(
                $"<color=#4EC9B0>+{addedCount} 新增</color>   " +
                $"<color=#DCDCAA>~{modifiedCount} 变更</color>   " +
                $"<color=#F44747>-{removedCount} 移除</color>   " +
                $"{diff.UnchangedCount} 未变",
                RichLabel);

            var totalDelta = diff.TotalNew - diff.TotalOld;
            var sign = totalDelta >= 0 ? "+" : "";
            EditorGUILayout.LabelField(
                $"总大小: {BuildUtils.FormatSize(diff.TotalOld)} → {BuildUtils.FormatSize(diff.TotalNew)}  " +
                $"(<b>{sign}{BuildUtils.FormatSize(totalDelta)}</b>)",
                RichLabel);

            if (diff.Entries.Count == 0)
            {
                EditorGUILayout.LabelField("  无变化", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.Space(2);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos,
                GUILayout.MinHeight(100), GUILayout.MaxHeight(300));

            foreach (var entry in diff.Entries)
                EditorGUILayout.LabelField($"  {FormatEntry(entry, rich: true)}", RichLabelWrap);

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("复制到剪贴板", GUILayout.Height(20)))
                CopyToClipboard();
        }

        void CopyToClipboard()
        {
            var diff = _ctx.LastDiff;
            var sb = new StringBuilder();
            sb.AppendLine(
                $"构建差异  总大小: {BuildUtils.FormatSize(diff.TotalOld)} → {BuildUtils.FormatSize(diff.TotalNew)}");
            sb.AppendLine();

            foreach (var entry in diff.Entries)
                sb.AppendLine(FormatEntry(entry, rich: false));

            sb.AppendLine($"\n未变: {diff.UnchangedCount}");
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log("[BuildTool] 构建差异已复制到剪贴板");
        }

        static string FormatEntry(DiffResult.Entry entry, bool rich)
        {
            switch (entry.Type)
            {
                case DiffResult.DiffType.Added:
                    var addText = $"+ {entry.Name}  ({BuildUtils.FormatSize(entry.NewSize)})";
                    return rich ? $"<color=#4EC9B0>{addText}</color>" : addText;
                case DiffResult.DiffType.Modified:
                    var d = entry.NewSize - entry.OldSize;
                    var ds = d >= 0 ? $"+{BuildUtils.FormatSize(d)}" : BuildUtils.FormatSize(d);
                    var modText = $"~ {entry.Name}  {BuildUtils.FormatSize(entry.OldSize)} → " +
                                  $"{BuildUtils.FormatSize(entry.NewSize)} ({ds})";
                    return rich ? $"<color=#DCDCAA>{modText}</color>" : modText;
                case DiffResult.DiffType.Removed:
                    var rmText = $"- {entry.Name}  ({BuildUtils.FormatSize(entry.OldSize)})";
                    return rich ? $"<color=#F44747>{rmText}</color>" : rmText;
                default:
                    return entry.Name;
            }
        }
    }
}
