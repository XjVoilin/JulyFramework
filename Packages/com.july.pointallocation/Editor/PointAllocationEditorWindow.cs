using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.PointAllocation.Editor
{
    public sealed class PointAllocationEditorWindow : EditorWindow
    {
        private const string LastAssetKey = "July.PointAllocation.Editor.LastAuthoringAsset";
        private PointAllocationGraphView _graphView;
        private IMGUIContainer _inspector;
        private ObjectField _assetField;
        private PointAllocationAuthoringAsset _authoring;
        private PointAllocationLayoutDirection _layoutDirection = PointAllocationLayoutDirection.LeftToRight;

        [MenuItem("JulyGF/加点图编辑器", priority = 1200)]
        public static void Open()
        {
            var window = GetWindow<PointAllocationEditorWindow>();
            window.titleContent = new GUIContent("加点图编辑器");
            window.minSize = new Vector2(900f, 560f);
            window.Show();
        }

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            if (!(EditorUtility.InstanceIDToObject(instanceId) is PointAllocationAuthoringAsset authoring))
                return false;

            Open();
            GetWindow<PointAllocationEditorWindow>().SetAuthoring(authoring);
            return true;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            var toolbar = new Toolbar();
            _assetField = new ObjectField("Authoring")
            {
                objectType = typeof(PointAllocationAuthoringAsset),
                allowSceneObjects = false
            };
            _assetField.style.minWidth = 280f;
            _assetField.RegisterValueChangedCallback(eventData =>
                SetAuthoring(eventData.newValue as PointAllocationAuthoringAsset));
            toolbar.Add(_assetField);
            toolbar.Add(new ToolbarButton(CreateAuthoringAsset) { text = "New" });
            toolbar.Add(new ToolbarButton(() =>
                _graphView?.AddNodeAt(_graphView.GetDefaultNodePosition())) { text = "Add Node" });

            var directionField = new EnumField(_layoutDirection);
            directionField.tooltip = "分层布局方向";
            directionField.RegisterValueChangedCallback(eventData =>
                _layoutDirection = (PointAllocationLayoutDirection)eventData.newValue);
            toolbar.Add(directionField);
            toolbar.Add(new ToolbarButton(() => ApplyLayout(false)) { text = "Layout All" });
            toolbar.Add(new ToolbarButton(() => ApplyLayout(true)) { text = "Layout Selected" });
            toolbar.Add(new ToolbarButton(ValidateAuthoring) { text = "Validate" });
            toolbar.Add(new ToolbarButton(ExportAuthoring) { text = "Export" });
            rootVisualElement.Add(toolbar);

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;

            _graphView = new PointAllocationGraphView(this);
            body.Add(_graphView);

            _inspector = new IMGUIContainer(DrawInspector);
            _inspector.style.width = 340f;
            _inspector.style.minWidth = 280f;
            _inspector.style.borderLeftWidth = 1f;
            _inspector.style.borderLeftColor = new Color(0.16f, 0.16f, 0.16f);
            _inspector.schedule.Execute(() => _inspector.MarkDirtyRepaint()).Every(150);
            body.Add(_inspector);
            rootVisualElement.Add(body);

            if (_authoring == null)
                _authoring = LoadLastAuthoring();
            SetAuthoring(_authoring);
        }

        internal void SetAuthoring(PointAllocationAuthoringAsset authoring)
        {
            _authoring = authoring;
            if (_assetField != null)
                _assetField.SetValueWithoutNotify(authoring);
            _graphView?.Load(authoring);

            if (authoring != null)
            {
                var path = AssetDatabase.GetAssetPath(authoring);
                if (!string.IsNullOrEmpty(path))
                    EditorPrefs.SetString(LastAssetKey, path);
            }
        }

        private static PointAllocationAuthoringAsset LoadLastAuthoring()
        {
            var path = EditorPrefs.GetString(LastAssetKey, string.Empty);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<PointAllocationAuthoringAsset>(path);
        }

        private void CreateAuthoringAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "创建加点图编辑源",
                "PointAllocationAuthoring",
                "asset",
                "请选择加点图编辑源资产的保存位置。");
            if (string.IsNullOrEmpty(path))
                return;

            var asset = CreateInstance<PointAllocationAuthoringAsset>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            SetAuthoring(asset);
        }

        private void ApplyLayout(bool selectedOnly)
        {
            if (_authoring == null)
                return;

            var nodes = _authoring.Nodes
                .Select(node => new PointAllocationLayoutNode(node.Id, node.Position))
                .ToArray();
            var connections = _authoring.CreateConnectionDefinitions();
            var result = PointAllocationLayeredLayout.Calculate(nodes, connections, _layoutDirection);
            if (!result.Success)
            {
                ShowNotification(new GUIContent(result.Error));
                return;
            }

            HashSet<int> selectedIds = null;
            if (selectedOnly)
            {
                selectedIds = _graphView.selection
                    .OfType<PointAllocationNodeView>()
                    .Select(view => view.Data.Id)
                    .ToHashSet();
                if (selectedIds.Count == 0)
                {
                    ShowNotification(new GUIContent("请先选择需要整理的节点。"));
                    return;
                }
            }

            _graphView.ApplyPositions(result.Positions, selectedIds);
        }

        private void ValidateAuthoring()
        {
            if (_authoring == null)
                return;
            ShowValidation(_authoring.ValidateDefinition(), "PointAllocation definition is valid.");
        }

        private void ExportAuthoring()
        {
            if (_authoring == null)
                return;

            if (PointAllocationExporter.ExportInteractive(_authoring, out var errors))
            {
                ShowNotification(new GUIContent("PointAllocation definition exported."));
                _assetField?.MarkDirtyRepaint();
                return;
            }

            if (errors != null && errors.Count > 0)
                ShowValidation(errors, null);
        }

        private void DrawInspector()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("PointAllocation Inspector", EditorStyles.boldLabel);
            if (_authoring == null)
            {
                EditorGUILayout.HelpBox("请选择或创建 PointAllocationAuthoringAsset。", MessageType.Info);
                return;
            }

            DrawAuthoringHeader();
            EditorGUILayout.Space(8f);

            var selectedNode = _graphView?.selection.OfType<PointAllocationNodeView>().FirstOrDefault();
            if (selectedNode != null)
            {
                DrawNodeInspector(selectedNode.Data);
                return;
            }

            var selectedEdge = _graphView?.selection.OfType<Edge>().FirstOrDefault();
            if (selectedEdge?.userData is PointAllocationConnectionAuthoringData connection)
            {
                DrawConnectionInspector(connection);
                return;
            }

            EditorGUILayout.HelpBox(
                $"Nodes: {_authoring.Nodes.Count}\nConnections: {_authoring.Connections.Count}\nNext NodeId: {_authoring.NextNodeId}",
                MessageType.None);
        }

        private void DrawAuthoringHeader()
        {
            EditorGUI.BeginChangeCheck();
            var definitionId = EditorGUILayout.IntField("Definition Id", _authoring.DefinitionId);
            var runtimeAsset = (PointAllocationGraphDefinitionAsset)EditorGUILayout.ObjectField(
                "Runtime Output",
                _authoring.RuntimeAsset,
                typeof(PointAllocationGraphDefinitionAsset),
                false);
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(_authoring, "Edit PointAllocation Authoring Settings");
            _authoring.SetDefinitionId(definitionId);
            _authoring.SetRuntimeAsset(runtimeAsset);
            EditorUtility.SetDirty(_authoring);
        }

        private void DrawNodeInspector(PointAllocationNodeAuthoringData node)
        {
            EditorGUILayout.LabelField("Node", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField("Node Id", node.Id);

            EditorGUI.BeginChangeCheck();
            var label = EditorGUILayout.TextField("Editor Label", node.Label);
            EditorGUILayout.LabelField("Editor Note");
            var note = EditorGUILayout.TextArea(node.Note, GUILayout.MinHeight(55f));
            var locked = EditorGUILayout.Toggle("Lock Position", node.Locked);
            var maxRank = Mathf.Max(1, EditorGUILayout.IntField("Max Rank", node.MaxRank));

            var costs = new int[maxRank];
            for (var index = 0; index < costs.Length; index++)
            {
                var previous = index < node.RankCosts.Count ? node.RankCosts[index] : 1;
                costs[index] = Mathf.Max(1, EditorGUILayout.IntField($"Rank {index + 1} Cost", previous));
            }

            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(_authoring, "Edit PointAllocation Node");
            node.SetLabel(label);
            node.SetNote(note);
            node.SetLocked(locked);
            _authoring.SetNodeMaxRank(node, maxRank);
            for (var index = 0; index < costs.Length; index++)
                node.SetRankCost(index, costs[index]);
            EditorUtility.SetDirty(_authoring);
            _graphView.RefreshNode(node);
        }

        private void DrawConnectionInspector(PointAllocationConnectionAuthoringData connection)
        {
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("From Node", connection.FromNodeId);
                EditorGUILayout.IntField("To Node", connection.ToNodeId);
            }

            var source = _authoring.FindNode(connection.FromNodeId);
            var sourceMaxRank = source?.MaxRank ?? 1;
            EditorGUI.BeginChangeCheck();
            var requiredRank = EditorGUILayout.IntSlider(
                "Required Rank",
                connection.RequiredRank,
                1,
                Mathf.Max(1, sourceMaxRank));
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(_authoring, "Edit PointAllocation Connection");
            connection.SetRequiredRank(requiredRank, sourceMaxRank);
            EditorUtility.SetDirty(_authoring);
        }

        private static void ShowValidation(
            IReadOnlyList<PointAllocationDefinitionError> errors,
            string successMessage)
        {
            if (errors == null || errors.Count == 0)
            {
                if (!string.IsNullOrEmpty(successMessage))
                    EditorUtility.DisplayDialog("PointAllocation Validation", successMessage, "OK");
                return;
            }

            var message = string.Join("\n", errors.Take(20).Select(error => $"• {error.Message}"));
            if (errors.Count > 20)
                message += $"\n… and {errors.Count - 20} more.";
            EditorUtility.DisplayDialog("PointAllocation Validation Failed", message, "OK");
        }
    }
}
