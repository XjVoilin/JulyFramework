using UnityEditor;
using UnityEngine;

namespace July.PointAllocation.Editor
{
    [CustomEditor(typeof(PointAllocationEditorWorkspace))]
    internal sealed class PointAllocationEditorWorkspaceInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var workspace = (PointAllocationEditorWorkspace)target;
            EditorGUILayout.HelpBox(
                "这是 JSON 的伴生 Editor 工作区，只保存画布与备注等编辑辅助数据。运行时不会读取该资产。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    "Graph JSON",
                    workspace.GraphJson,
                    typeof(UnityEngine.TextAsset),
                    false);
                EditorGUILayout.IntField("Editor Nodes", workspace.Nodes.Count);
                EditorGUILayout.IntField("Next Node Id", workspace.NextNodeId);
            }

            if (GUILayout.Button("打开加点图编辑器") && workspace.GraphJson != null)
            {
                PointAllocationEditorWindow.Open();
                EditorWindow.GetWindow<PointAllocationEditorWindow>()
                    .SetJsonAsset(workspace.GraphJson);
            }
        }
    }
}
