using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    internal sealed class ColliderItemSettingsWindow : EditorWindow
    {
        ColliderOverviewWindow owner;
        ColliderRow row;
        string targetId, targetName, error;
        ColliderGenerationSettings draft;
        bool inherit;
        Vector2 scroll;

        internal static void Open(ColliderOverviewWindow owner, ColliderRow row, int index)
        {
            var window = CreateInstance<ColliderItemSettingsWindow>();
            var target = owner.Workspace.Profile.Targets[index];
            window.owner = owner; window.row = row; window.targetId = target.Id; window.targetName = target.Name;
            window.draft = row.EffectiveSettings(index); window.inherit = row.Recipe(index) == null;
            window.titleContent = new GUIContent("逐项调参");
            window.minSize = new Vector2(430, 260); window.maxSize = new Vector2(620, 480);
            window.ShowUtility();
        }

        void OnGUI()
        {
            if (owner == null || owner.Workspace.IsDisposed || !owner.Workspace.Items.Contains(row)) { Close(); return; }
            using var content = new EditorGUILayout.ScrollViewScope(scroll); scroll = content.scrollPosition;
            EditorGUILayout.LabelField(row.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(targetName + " · " + (inherit ? "继承默认参数" : "此项独立参数"), EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(owner.Workspace.Busy || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                draft.TargetCount = EditorGUILayout.IntSlider(new GUIContent(draft.Goal == ColliderGeometryGoal.Structure ? "结构贴合碰撞体数上限" : "整体包围碰撞体数上限", "当前输出组允许使用的碰撞体总数量，包含原生碰撞体与凸包，最终可能少于预算；增加预算通常增加计算时间。"), draft.TargetCount, 1, draft.Goal == ColliderGeometryGoal.Structure ? PickingProxy.GenerationSettings.ConvexCountLimit : 128);
                if (draft.Goal == ColliderGeometryGoal.Structure)
                {
                    ColliderGenerationSettingsGUI.DrawPickingMeshBudget(draft);
                }
                else
                    draft.SilhouetteErrorPercent = EditorGUILayout.Slider(new GUIContent("允许轮廓误差（%）", "减小允许误差要求更贴近轮廓；0表示保留初始拟合，不执行轮廓简化。"), draft.SilhouetteErrorPercent, 0, 40);
                if (EditorGUI.EndChangeCheck()) { inherit = false; error = null; }
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("恢复默认", "恢复本栏默认参数，保存后生效。")))
                    {
                        draft = owner.Workspace.Profile.Targets.Single(t => t.Id == targetId).Settings.Copy();
                        inherit = true; error = null;
                    }
                    if (GUILayout.Button("保存")) Save(false);
                    if (GUILayout.Button("保存并生成本栏")) Save(true);
                }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        void Save(bool generate)
        {
            try
            {
                if (inherit) owner.Workspace.ResetGenerationSettings(row, targetId);
                else owner.Workspace.SetGenerationSettings(row, targetId, draft.TargetCount, draft.SilhouetteErrorPercent,
                    draft.MaxHullVertices, draft.PickingInwardErrorPercent, draft.PickingShapes, draft.NativeShapeErrorPercent, draft.NativeClickChangePercent);
                var window = owner; var item = row; var target = targetId;
                Close();
                if (generate) window.Generate(new[] { item }, ColliderTargetSelection.Only(target));
            }
            catch (Exception failure) { error = failure.Message; }
        }
    }
}
