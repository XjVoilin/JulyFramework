using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LitJson;
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
        private const string LastJsonKey = "July.PointAllocation.Editor.LastJson";
        private PointAllocationGraphView _graphView;
        private IMGUIContainer _inspector;
        private ObjectField _jsonField;
        private TextAsset _jsonAsset;
        private PointAllocationEditorWorkspace _workspace;
        private PointAllocationEditorDocument _document;
        private PointAllocationLayoutDirection _layoutDirection = PointAllocationLayoutDirection.LeftToRight;

        [MenuItem("JulyGF/加点图编辑器", priority = 1200)]
        public static void Open()
        {
            var window = GetWindow<PointAllocationEditorWindow>();
            window.titleContent = new GUIContent("加点图编辑器");
            window.minSize = new Vector2(900f, 560f);
            window.saveChangesMessage = "加点图 JSON 尚未保存，是否保存？";
            window.Show();
        }

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId);
            TextAsset json = null;
            if (asset is PointAllocationEditorWorkspace workspace)
                json = workspace.GraphJson;
            else if (asset is TextAsset textAsset &&
                     string.Equals(
                         Path.GetExtension(AssetDatabase.GetAssetPath(textAsset)),
                         ".json",
                         System.StringComparison.OrdinalIgnoreCase))
                json = textAsset;

            if (json == null)
                return false;

            try
            {
                ReadGraph(json.text);
            }
            catch (Exception)
            {
                return false;
            }

            Open();
            GetWindow<PointAllocationEditorWindow>().SetJsonAsset(json);
            return true;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            var toolbar = new Toolbar();
            _jsonField = new ObjectField("Graph JSON")
            {
                objectType = typeof(TextAsset),
                allowSceneObjects = false
            };
            _jsonField.style.minWidth = 300f;
            _jsonField.RegisterValueChangedCallback(eventData =>
            {
                if (!TrySetJsonAsset(eventData.newValue as TextAsset))
                    _jsonField.SetValueWithoutNotify(_jsonAsset);
            });
            toolbar.Add(_jsonField);
            toolbar.Add(new ToolbarButton(CreateJson) { text = "New JSON" });
            toolbar.Add(new ToolbarButton(() => SaveDocument(true)) { text = "Save JSON" });
            toolbar.Add(new ToolbarButton(() =>
                _graphView?.AddNodeAt(_graphView.GetDefaultNodePosition())) { text = "Add Node" });

            var directionField = new EnumField(_layoutDirection);
            directionField.tooltip = "分层布局方向";
            directionField.RegisterValueChangedCallback(eventData =>
                _layoutDirection = (PointAllocationLayoutDirection)eventData.newValue);
            toolbar.Add(directionField);
            toolbar.Add(new ToolbarButton(() => ApplyLayout(false)) { text = "Layout All" });
            toolbar.Add(new ToolbarButton(() => ApplyLayout(true)) { text = "Layout Selected" });
            toolbar.Add(new ToolbarButton(ValidateDocument) { text = "Validate" });
            rootVisualElement.Add(toolbar);

            var body = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexGrow = 1f
                }
            };

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

            if (_jsonAsset == null)
                _jsonAsset = LoadLastJson();
            SetJsonAsset(_jsonAsset);
        }

        public override void SaveChanges()
        {
            if (SaveDocument(true))
                base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            hasUnsavedChanges = false;
            LoadJsonAsset(_jsonAsset);
            base.DiscardChanges();
        }

        internal void SetJsonAsset(TextAsset json) => TrySetJsonAsset(json, false);

        internal void OnDocumentChanged()
        {
            if (_document != null)
                hasUnsavedChanges = true;
        }

        private bool TrySetJsonAsset(TextAsset json, bool askToSave = true)
        {
            if (json == _jsonAsset && _document != null)
                return true;

            if (askToSave && hasUnsavedChanges)
            {
                var choice = EditorUtility.DisplayDialogComplex(
                    "Unsaved PointAllocation JSON",
                    "当前加点图 JSON 尚未保存。",
                    "Save",
                    "Cancel",
                    "Discard");
                if (choice == 1)
                    return false;
                if (choice == 0 && !SaveDocument(true))
                    return false;
                if (choice == 2)
                    hasUnsavedChanges = false;
            }

            return LoadJsonAsset(json);
        }

        private bool LoadJsonAsset(TextAsset json)
        {
            if (json == null)
            {
                _jsonAsset = null;
                _workspace = null;
                _document = null;
                _jsonField?.SetValueWithoutNotify(null);
                _graphView?.Load(null);
                hasUnsavedChanges = false;
                return true;
            }

            PointAllocationGraph graph;
            try
            {
                graph = ReadGraph(json.text);
            }
            catch (Exception exception)
            {
                ShowValidationFailure(exception.Message);
                return false;
            }

            var workspace = GetOrCreateWorkspace(json);
            if (workspace == null)
            {
                EditorUtility.DisplayDialog(
                    "PointAllocation Editor",
                    "无法为该 JSON 创建或加载伴生 Editor Workspace。",
                    "OK");
                return false;
            }

            var document = new PointAllocationEditorDocument(graph, workspace);
            _jsonAsset = json;
            _workspace = workspace;
            _document = document;
            _jsonField?.SetValueWithoutNotify(json);
            EditorUtility.SetDirty(_workspace);
            _graphView?.Load(_document);
            hasUnsavedChanges = false;

            var path = AssetDatabase.GetAssetPath(json);
            if (!string.IsNullOrEmpty(path))
                EditorPrefs.SetString(LastJsonKey, path);
            return true;
        }

        private static TextAsset LoadLastJson()
        {
            var path = EditorPrefs.GetString(LastJsonKey, string.Empty);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }

        private void CreateJson()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "创建加点图 JSON",
                "PointAllocationGraph",
                "json",
                "请选择加点图 JSON 的保存位置。");
            if (string.IsNullOrEmpty(path))
                return;

            var node = new PointAllocationNode(1, 1, new[] { 1 });
            var graph = new PointAllocationGraph(
                1,
                new[] { node },
                Array.Empty<PointAllocationConnection>());

            File.WriteAllText(
                Path.GetFullPath(path),
                WriteGraph(graph));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Selection.activeObject = asset;
            TrySetJsonAsset(asset);
        }

        private bool SaveDocument(bool showResult)
        {
            if (_document == null || _jsonAsset == null)
                return false;
            PointAllocationGraph graph;
            string output;
            try
            {
                graph = _document.CreateGraph();
                output = WriteGraph(graph);
            }
            catch (ArgumentException exception)
            {
                ShowValidationFailure(exception.Message);
                return false;
            }

            var path = AssetDatabase.GetAssetPath(_jsonAsset);
            if (string.IsNullOrEmpty(path))
                return false;

            File.WriteAllText(
                Path.GetFullPath(path),
                output);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            _jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            _workspace.SetGraphJson(_jsonAsset);
            EditorUtility.SetDirty(_workspace);
            AssetDatabase.SaveAssets();
            _jsonField?.SetValueWithoutNotify(_jsonAsset);
            hasUnsavedChanges = false;
            if (showResult)
                ShowNotification(new GUIContent("PointAllocation JSON saved."));
            return true;
        }

        private void ApplyLayout(bool selectedOnly)
        {
            if (_document == null)
                return;

            var nodes = _document.Nodes
                .Select(node => new PointAllocationLayoutNode(node.Id, node.EditorData.Position))
                .ToArray();
            var connections = _document.Connections
                .Select(connection => connection.ToGraph())
                .ToArray();
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

        private static PointAllocationGraph ReadGraph(string json)
        {
            PointAllocationGraph graph;
            try
            {
                graph = JsonMapper.ToObject<PointAllocationGraph>(json)
                        ?? throw new ArgumentException("加点图 JSON 中没有有效对象。");
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("加点图 JSON 格式无效。", exception);
            }
            PointAllocationGraphValidator.Validate(graph);
            return graph;
        }

        private static string WriteGraph(PointAllocationGraph graph)
        {
            PointAllocationGraphValidator.Validate(graph);
            var output = new StringBuilder();
            var writer = new JsonWriter(output) { PrettyPrint = true };
            JsonMapper.ToJson(graph, writer);
            return output.ToString();
        }

        private void ValidateDocument()
        {
            if (_document == null)
                return;
            try
            {
                var graph = _document.CreateGraph();
                PointAllocationGraphValidator.Validate(graph);
                EditorUtility.DisplayDialog(
                    "PointAllocation Validation",
                    "加点图校验通过。",
                    "OK");
            }
            catch (ArgumentException exception)
            {
                ShowValidationFailure(exception.Message);
            }
        }

        private void DrawInspector()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("PointAllocation Inspector", EditorStyles.boldLabel);
            if (_document == null)
            {
                EditorGUILayout.HelpBox("请选择或创建加点图 JSON。", MessageType.Info);
                return;
            }

            EditorGUI.BeginChangeCheck();
            var graphId = EditorGUILayout.IntField("Graph Id", _document.GraphId);
            if (EditorGUI.EndChangeCheck())
            {
                _document.SetGraphId(graphId);
                OnDocumentChanged();
            }
            EditorGUILayout.Space(8f);

            var selectedNode = _graphView?.selection.OfType<PointAllocationNodeView>().FirstOrDefault();
            if (selectedNode != null)
            {
                DrawNodeInspector(selectedNode.Data);
                return;
            }

            var selectedEdge = _graphView?.selection.OfType<Edge>().FirstOrDefault();
            if (selectedEdge?.userData is PointAllocationEditableConnection connection)
            {
                DrawConnectionInspector(connection);
                return;
            }

            EditorGUILayout.HelpBox(
                $"Nodes: {_document.Nodes.Count}\nConnections: {_document.Connections.Count}\nNext NodeId: {_workspace.NextNodeId}",
                MessageType.None);
        }

        private void DrawNodeInspector(PointAllocationEditableNode node)
        {
            EditorGUILayout.LabelField("Node", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField("Node Id", node.Id);

            var metadata = node.EditorData;
            EditorGUI.BeginChangeCheck();
            var label = EditorGUILayout.TextField("Editor Label", metadata.Label);
            EditorGUILayout.LabelField("Editor Note");
            var note = EditorGUILayout.TextArea(metadata.Note, GUILayout.MinHeight(55f));
            var locked = EditorGUILayout.Toggle("Lock Position", metadata.Locked);
            var maxLevel = Mathf.Max(1, EditorGUILayout.IntField("Max Level", node.MaxLevel));
            var costs = new int[maxLevel];
            for (var level = 0; level < costs.Length; level++)
            {
                var previous = level < node.UpgradeCosts.Count ? node.UpgradeCosts[level] : 1;
                costs[level] = Mathf.Max(0, EditorGUILayout.IntField(
                    $"Level {level} → {level + 1} Cost",
                    previous));
            }
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(_workspace, "Edit PointAllocation Node Metadata");
            metadata.SetLabel(label);
            metadata.SetNote(note);
            metadata.SetLocked(locked);
            _document.SetNodeMaxLevel(node, maxLevel);
            for (var level = 0; level < costs.Length; level++)
                node.SetUpgradeCost(level, costs[level]);
            EditorUtility.SetDirty(_workspace);
            OnDocumentChanged();
            _graphView.RefreshNode(node);
        }

        private void DrawConnectionInspector(PointAllocationEditableConnection connection)
        {
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("From Node", connection.FromNodeId);
                EditorGUILayout.IntField("To Node", connection.ToNodeId);
            }

            var sourceMaxLevel = _document.FindNode(connection.FromNodeId)?.MaxLevel ?? 1;
            EditorGUI.BeginChangeCheck();
            var requiredLevel = EditorGUILayout.IntSlider(
                "Required Level",
                connection.RequiredLevel,
                1,
                Mathf.Max(1, sourceMaxLevel));
            if (!EditorGUI.EndChangeCheck())
                return;

            connection.SetRequiredLevel(requiredLevel, sourceMaxLevel);
            OnDocumentChanged();
        }

        private static PointAllocationEditorWorkspace GetOrCreateWorkspace(TextAsset json)
        {
            var jsonPath = AssetDatabase.GetAssetPath(json);
            if (string.IsNullOrEmpty(jsonPath))
                return null;
            var folder = Path.GetDirectoryName(jsonPath)?.Replace('\\', '/');
            var fileName = Path.GetFileNameWithoutExtension(jsonPath);
            var workspacePath = $"{folder}/{fileName}.PointAllocationEditor.asset";
            var workspace = AssetDatabase.LoadAssetAtPath<PointAllocationEditorWorkspace>(workspacePath);
            if (workspace != null)
                return workspace;
            if (AssetDatabase.LoadMainAssetAtPath(workspacePath) != null)
                return null;

            workspace = CreateInstance<PointAllocationEditorWorkspace>();
            workspace.SetGraphJson(json);
            AssetDatabase.CreateAsset(workspace, workspacePath);
            AssetDatabase.SaveAssets();
            return workspace;
        }

        private static void ShowValidationFailure(string message)
        {
            EditorUtility.DisplayDialog("PointAllocation Validation Failed", message, "OK");
        }
    }
}
