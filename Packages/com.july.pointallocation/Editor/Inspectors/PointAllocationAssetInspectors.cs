using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.PointAllocation.Editor
{
    [CustomEditor(typeof(PointAllocationAuthoringAsset))]
    internal sealed class PointAllocationAuthoringAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var authoring = (PointAllocationAuthoringAsset)target;
            EditorGUILayout.HelpBox(
                "这是加点图编辑器的源资产。节点、连线和布局请在图编辑器中修改。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Definition Id", authoring.DefinitionId);
                EditorGUILayout.IntField("Nodes", authoring.Nodes.Count);
                EditorGUILayout.IntField("Connections", authoring.Connections.Count);
                EditorGUILayout.IntField("Next Node Id", authoring.NextNodeId);
                EditorGUILayout.ObjectField(
                    "Runtime Output",
                    authoring.RuntimeAsset,
                    typeof(PointAllocationGraphDefinitionAsset),
                    false);
            }

            if (GUILayout.Button("打开加点图编辑器"))
            {
                PointAllocationEditorWindow.Open();
                var window = EditorWindow.GetWindow<PointAllocationEditorWindow>();
                window.SetAuthoring(authoring);
            }
        }
    }

    [CustomEditor(typeof(PointAllocationGraphDefinitionAsset))]
    internal sealed class PointAllocationGraphDefinitionAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (PointAllocationGraphDefinitionAsset)target;
            EditorGUILayout.HelpBox(
                "这是加点图编辑器生成的运行时定义。请修改 AuthoringAsset 后重新导出，不要直接编辑该资产。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Definition Id", asset.DefinitionId);
                EditorGUILayout.IntField("Nodes", asset.Nodes.Count);
                EditorGUILayout.IntField("Connections", asset.Connections.Count);
            }

            if (asset.TryCreateDefinition(out _, out var errors))
            {
                EditorGUILayout.HelpBox("Runtime definition is valid.", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    string.Join("\n", errors.Take(10).Select(error => error.Message)),
                    MessageType.Error);
            }
        }
    }
}
