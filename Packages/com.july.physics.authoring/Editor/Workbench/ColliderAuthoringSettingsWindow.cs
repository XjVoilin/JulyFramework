using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    internal sealed class ColliderAuthoringSettingsWindow : EditorWindow
    {
        ColliderWorkspace workspace;
        ColliderAuthoringProfile draft;
        Action updated;
        string error;
        Vector2 scroll;
        internal static void Open(ColliderWorkspace workspace, Action updated)
        {
            var window = CreateInstance<ColliderAuthoringSettingsWindow>();
            window.workspace = workspace; window.draft = workspace.Configuration; window.updated = updated;
            window.titleContent = new GUIContent(workspace.Profile.Title + " · 制作设置");
            window.minSize = new Vector2(480, 300); window.ShowUtility();
        }
        void OnGUI()
        {
            if (workspace == null || workspace.IsDisposed) { Close(); return; }
            using (new EditorGUI.DisabledScope(workspace.Busy))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                DrawPerformance();
                EditorGUILayout.Space(12);
                EditorGUILayout.LabelField("输出配置", EditorStyles.boldLabel);
                draft.MeshDirectory = EditorGUILayout.TextField("生成Mesh目录", draft.MeshDirectory);
                if (GUILayout.Button("选择目录"))
                {
                    string selected = EditorUtility.OpenFolderPanel("生成Mesh目录", Application.dataPath, "");
                    if (selected.Length > 0) draft.MeshDirectory = FileUtil.GetProjectRelativePath(selected);
                }
                using (new EditorGUI.DisabledScope(!draft.EditableTargets))
                {
                    draft.SourcePath = EditorGUILayout.TextField("模型根路径（空为根）", draft.SourcePath);
                    for (int i = 0; i < draft.Targets.Length; i++)
                    {
                        var target = draft.Targets[i];
                        EditorGUILayout.Space(); target.Name = EditorGUILayout.TextField("输出名称", target.Name);
                        target.Path = EditorGUILayout.TextField("输出节点路径", target.Path);
                        var goal = (ColliderGeometryGoal)EditorGUILayout.EnumPopup("生成方式", target.Settings.Goal);
                        if (goal != target.Settings.Goal) target.ChangeGoal(goal);
                        ColliderGenerationSettingsGUI.DrawParameters(target.Settings);
                        if (draft.Targets.Length > 1 && GUILayout.Button("移除此输出"))
                        { draft.Targets = draft.Targets.Where((t, index) => index != i).ToArray(); GUIUtility.ExitGUI(); }
                    }
                    if (GUILayout.Button("添加输出目标"))
                        draft.Targets = draft.Targets.Concat(new[] { new ColliderOutputTarget("target-" + Guid.NewGuid().ToString("N"), "输出" + (draft.Targets.Length + 1), "Colliders" + (draft.Targets.Length + 1), ColliderGeometryGoal.Structure) }).ToArray();
                }
                EditorGUILayout.EndScrollView();
                if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
                if (GUILayout.Button("保存设置"))
                {
                    try { workspace.Configure(draft); updated(); Close(); }
                    catch (Exception failure) { error = failure.Message; }
                }
            }
        }
        void DrawPerformance()
        {
            EditorGUILayout.LabelField("性能提醒", EditorStyles.boldLabel);
            var settings = draft.Performance;
            settings.Enabled = EditorGUILayout.Toggle("启用成本提醒", settings.Enabled);
            EditorGUILayout.HelpBox("按单件物品的单个输出组统计。碰撞体数包含原生碰撞体与凸包；网格顶点数不计盒、球、胶囊。只影响提醒、筛选和排序，保存后生效，无需重新生成。类型、缺失网格及单凸包24顶点上限检查始终保留。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!settings.Enabled))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("达到以下条件时提示", GUILayout.Width(180));
                    GUILayout.Label("提醒"); GUILayout.Label("重点检查");
                }
                DrawThreshold("整体包围碰撞体数 >", ref settings.EnvelopeReminder, ref settings.EnvelopeAttention);
                DrawThreshold("结构贴合碰撞体数 >", ref settings.StructureReminder, ref settings.StructureAttention);
                DrawThreshold("结构贴合凸包数 ≥", ref settings.ConvexReminder, ref settings.ConvexAttention);
                DrawThreshold("单凸包顶点数 >", ref settings.HullVerticesReminder, ref settings.HullVerticesAttention);
                DrawThreshold("结构贴合网格总顶点数 >", ref settings.TotalVerticesReminder, ref settings.TotalVerticesAttention);
            }
            if (GUILayout.Button("恢复性能提醒默认值")) draft.Performance = new ColliderPerformanceSettings();
        }
        static void DrawThreshold(string label, ref int reminder, ref int attention)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(180));
                reminder = EditorGUILayout.IntField(reminder);
                attention = EditorGUILayout.IntField(attention);
            }
        }
    }
}
