using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace July.PointAllocation.Editor
{
    internal sealed class PointAllocationNodeView : Node
    {
        public PointAllocationEditableNode Data { get; }
        public Port InputPort { get; }
        public Port OutputPort { get; }

        public PointAllocationNodeView(PointAllocationEditableNode data)
        {
            Data = data ?? throw new ArgumentNullException(
                nameof(data),
                "节点编辑数据 data 不能为 null。");
            viewDataKey = $"point-allocation-node-{data.Id}";

            InputPort = InstantiatePort(
                Orientation.Horizontal,
                Direction.Input,
                Port.Capacity.Multi,
                typeof(bool));
            InputPort.portName = "Prerequisites";
            inputContainer.Add(InputPort);

            OutputPort = InstantiatePort(
                Orientation.Horizontal,
                Direction.Output,
                Port.Capacity.Multi,
                typeof(bool));
            OutputPort.portName = "Unlocks";
            outputContainer.Add(OutputPort);

            RefreshTitle();
            SetPosition(new Rect(data.EditorData.Position, new Vector2(230f, 130f)));
            RefreshExpandedState();
            RefreshPorts();
        }

        public void RefreshTitle()
        {
            var metadata = Data.EditorData;
            var baseTitle = string.IsNullOrWhiteSpace(metadata.Label)
                ? $"Node {Data.Id}"
                : $"{metadata.Label}  [{Data.Id}]";
            title = metadata.Locked ? $"🔒 {baseTitle}" : baseTitle;
            tooltip = string.IsNullOrWhiteSpace(metadata.Note) ? null : metadata.Note;
        }
    }

    internal sealed class PointAllocationGraphView : GraphView
    {
        private readonly PointAllocationEditorWindow _window;
        private readonly Dictionary<int, PointAllocationNodeView> _nodeViews =
            new Dictionary<int, PointAllocationNodeView>();
        private PointAllocationEditorDocument _document;
        private bool _suppressChanges;
        private Vector2 _lastContentMousePosition;

        public PointAllocationGraphView(PointAllocationEditorWindow window)
        {
            _window = window;
            name = "PointAllocation Graph";
            style.flexGrow = 1f;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            graphViewChanged = OnGraphViewChanged;
            viewTransformChanged += OnViewTransformChanged;
            RegisterCallback<MouseMoveEvent>(eventData =>
                _lastContentMousePosition = contentViewContainer.WorldToLocal(eventData.mousePosition));
            RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
        }

        public void Load(PointAllocationEditorDocument document)
        {
            _suppressChanges = true;
            try
            {
                DeleteElements(graphElements.ToList());
                _nodeViews.Clear();
                _document = document;
                if (_document == null)
                    return;

                for (var index = 0; index < _document.Nodes.Count; index++)
                    AddNodeView(_document.Nodes[index]);

                for (var index = 0; index < _document.Connections.Count; index++)
                {
                    var connection = _document.Connections[index];
                    if (!_nodeViews.TryGetValue(connection.FromNodeId, out var from) ||
                        !_nodeViews.TryGetValue(connection.ToNodeId, out var to))
                    {
                        continue;
                    }

                    var edge = from.OutputPort.ConnectTo(to.InputPort);
                    edge.userData = connection;
                    AddElement(edge);
                }

                UpdateViewTransform(
                    _document.Workspace.ViewPosition,
                    _document.Workspace.ViewScale);
            }
            finally
            {
                _suppressChanges = false;
            }
        }

        public PointAllocationNodeView AddNodeAt(Vector2 position)
        {
            if (_document == null)
                return null;

            Undo.RecordObject(_document.Workspace, "Add PointAllocation Node Metadata");
            var data = _document.AddNode(position);
            var view = AddNodeView(data);
            EditorUtility.SetDirty(_document.Workspace);
            _window.OnDocumentChanged();
            ClearSelection();
            AddToSelection(view);
            return view;
        }

        public Vector2 GetDefaultNodePosition()
        {
            var worldCenter = worldBound.center;
            return contentViewContainer.WorldToLocal(worldCenter) - new Vector2(115f, 65f);
        }

        public void RefreshNode(PointAllocationEditableNode data)
        {
            if (data != null && _nodeViews.TryGetValue(data.Id, out var view))
                view.RefreshTitle();
        }

        public void ApplyPositions(
            IReadOnlyDictionary<int, Vector2> positions,
            ISet<int> includedNodeIds)
        {
            if (_document == null || positions == null)
                return;

            Undo.RecordObject(_document.Workspace, "Auto Layout PointAllocation Graph");
            _suppressChanges = true;
            try
            {
                foreach (var pair in positions)
                {
                    if (includedNodeIds != null && !includedNodeIds.Contains(pair.Key))
                        continue;
                    var data = _document.FindNode(pair.Key);
                    if (data == null || data.EditorData.Locked ||
                        !_nodeViews.TryGetValue(pair.Key, out var view))
                    {
                        continue;
                    }

                    data.EditorData.SetPosition(pair.Value);
                    var rect = view.GetPosition();
                    rect.position = pair.Value;
                    view.SetPosition(rect);
                }
            }
            finally
            {
                _suppressChanges = false;
            }
            EditorUtility.SetDirty(_document.Workspace);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var ports = new List<Port>();
            foreach (var port in this.ports)
            {
                if (port != startPort &&
                    port.direction != startPort.direction &&
                    port.node != startPort.node)
                {
                    ports.Add(port);
                }
            }
            return ports;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent eventData)
        {
            if (_document != null)
            {
                eventData.menu.AppendAction(
                    "Add PointAllocation Node",
                    _ => AddNodeAt(_lastContentMousePosition));
                eventData.menu.AppendSeparator();
            }
            base.BuildContextualMenu(eventData);
        }

        private PointAllocationNodeView AddNodeView(PointAllocationEditableNode data)
        {
            var view = new PointAllocationNodeView(data);
            _nodeViews.Add(data.Id, view);
            AddElement(view);
            return view;
        }

        private void OnMouseDown(MouseDownEvent eventData)
        {
            if (_document == null || eventData.button != 0 || eventData.clickCount != 2)
                return;

            if (eventData.target is VisualElement target &&
                (target is GraphElement || target.GetFirstAncestorOfType<GraphElement>() != null))
            {
                return;
            }

            AddNodeAt(contentViewContainer.WorldToLocal(eventData.mousePosition));
            eventData.StopImmediatePropagation();
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_suppressChanges || _document == null)
                return change;

            if (change.movedElements != null && change.movedElements.Count > 0)
            {
                Undo.RecordObject(_document.Workspace, "Move PointAllocation Nodes");
                for (var index = 0; index < change.movedElements.Count; index++)
                {
                    if (change.movedElements[index] is PointAllocationNodeView nodeView)
                        nodeView.Data.EditorData.SetPosition(nodeView.GetPosition().position);
                }
                EditorUtility.SetDirty(_document.Workspace);
            }

            if (change.edgesToCreate != null)
            {
                for (var index = change.edgesToCreate.Count - 1; index >= 0; index--)
                {
                    var edge = change.edgesToCreate[index];
                    if (!(edge.output?.node is PointAllocationNodeView from) ||
                        !(edge.input?.node is PointAllocationNodeView to) ||
                        !_document.TryAddConnection(from.Data.Id, to.Data.Id, out var connection))
                    {
                        change.edgesToCreate.RemoveAt(index);
                        _window.ShowNotification(new GUIContent("连接重复、无效或会形成有向环。"));
                        continue;
                    }

                    edge.userData = connection;
                    _window.OnDocumentChanged();
                }
            }

            if (change.elementsToRemove != null && change.elementsToRemove.Count > 0)
            {
                for (var index = change.elementsToRemove.Count - 1; index >= 0; index--)
                {
                    switch (change.elementsToRemove[index])
                    {
                        case PointAllocationNodeView nodeView:
                            Undo.RecordObject(_document.Workspace, "Delete PointAllocation Node Metadata");
                            if (!_document.RemoveNode(nodeView.Data.Id))
                            {
                                change.elementsToRemove.RemoveAt(index);
                                _window.ShowNotification(new GUIContent("加点图必须至少保留一个节点。"));
                                continue;
                            }
                            _nodeViews.Remove(nodeView.Data.Id);
                            EditorUtility.SetDirty(_document.Workspace);
                            _window.OnDocumentChanged();
                            break;
                        case Edge edge when edge.userData is PointAllocationEditableConnection connection:
                            _document.RemoveConnection(connection.FromNodeId, connection.ToNodeId);
                            _window.OnDocumentChanged();
                            break;
                    }
                }
            }

            return change;
        }

        private void OnViewTransformChanged(GraphView graphView)
        {
            if (_suppressChanges || _document == null)
                return;
            Undo.RecordObject(_document.Workspace, "Change PointAllocation View");
            _document.Workspace.SetViewTransform(viewTransform.position, viewTransform.scale);
            EditorUtility.SetDirty(_document.Workspace);
        }
    }
}
