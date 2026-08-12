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
        public PointAllocationNodeAuthoringData Data { get; }
        public Port InputPort { get; }
        public Port OutputPort { get; }

        public PointAllocationNodeView(PointAllocationNodeAuthoringData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            viewDataKey = $"point-allocation-node-{data.Id}";

            InputPort = InstantiatePort(
                Orientation.Horizontal,
                Direction.Input,
                Port.Capacity.Multi,
                typeof(bool));
            InputPort.portName = "Prerequisites";
            InputPort.userData = this;
            inputContainer.Add(InputPort);

            OutputPort = InstantiatePort(
                Orientation.Horizontal,
                Direction.Output,
                Port.Capacity.Multi,
                typeof(bool));
            OutputPort.portName = "Unlocks";
            OutputPort.userData = this;
            outputContainer.Add(OutputPort);

            RefreshTitle();
            SetPosition(new Rect(data.Position, new Vector2(230f, 130f)));
            RefreshExpandedState();
            RefreshPorts();
        }

        public void RefreshTitle()
        {
            var baseTitle = string.IsNullOrWhiteSpace(Data.Label)
                ? $"Node {Data.Id}"
                : $"{Data.Label}  [{Data.Id}]";
            title = Data.Locked ? $"🔒 {baseTitle}" : baseTitle;
            tooltip = string.IsNullOrWhiteSpace(Data.Note) ? null : Data.Note;

        }
    }

    internal sealed class PointAllocationGraphView : GraphView
    {
        private readonly EditorWindow _window;
        private readonly Dictionary<int, PointAllocationNodeView> _nodeViews =
            new Dictionary<int, PointAllocationNodeView>();
        private PointAllocationAuthoringAsset _authoring;
        private bool _suppressChanges;
        private Vector2 _lastContentMousePosition;

        public PointAllocationAuthoringAsset Authoring => _authoring;

        public PointAllocationGraphView(EditorWindow window)
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
            {
                _lastContentMousePosition = contentViewContainer.WorldToLocal(eventData.mousePosition);
            });
        }

        public void Load(PointAllocationAuthoringAsset authoring)
        {
            _suppressChanges = true;
            try
            {
                DeleteElements(graphElements.ToList());
                _nodeViews.Clear();
                _authoring = authoring;
                if (_authoring == null)
                    return;

                _authoring.Normalize();
                for (var index = 0; index < _authoring.Nodes.Count; index++)
                    AddNodeView(_authoring.Nodes[index]);

                for (var index = 0; index < _authoring.Connections.Count; index++)
                {
                    var connection = _authoring.Connections[index];
                    if (!_nodeViews.TryGetValue(connection.FromNodeId, out var from) ||
                        !_nodeViews.TryGetValue(connection.ToNodeId, out var to))
                    {
                        continue;
                    }

                    var edge = from.OutputPort.ConnectTo(to.InputPort);
                    edge.userData = connection;
                    AddElement(edge);
                }

                UpdateViewTransform(_authoring.ViewPosition, _authoring.ViewScale);
            }
            finally
            {
                _suppressChanges = false;
            }
        }

        public PointAllocationNodeView AddNodeAt(Vector2 position)
        {
            if (_authoring == null)
                return null;

            Undo.RecordObject(_authoring, "Add PointAllocation Node");
            var data = _authoring.AddNode(position);
            var view = AddNodeView(data);
            EditorUtility.SetDirty(_authoring);
            ClearSelection();
            AddToSelection(view);
            return view;
        }

        public Vector2 GetDefaultNodePosition()
        {
            var worldCenter = worldBound.center;
            return contentViewContainer.WorldToLocal(worldCenter) - new Vector2(115f, 65f);
        }

        public void RefreshNode(PointAllocationNodeAuthoringData data)
        {
            if (data != null && _nodeViews.TryGetValue(data.Id, out var view))
                view.RefreshTitle();
        }

        public void ApplyPositions(
            IReadOnlyDictionary<int, Vector2> positions,
            ISet<int> includedNodeIds)
        {
            if (_authoring == null || positions == null)
                return;

            Undo.RecordObject(_authoring, "Auto Layout PointAllocation Graph");
            _suppressChanges = true;
            try
            {
                foreach (var pair in positions)
                {
                    if (includedNodeIds != null && !includedNodeIds.Contains(pair.Key))
                        continue;
                    var data = _authoring.FindNode(pair.Key);
                    if (data == null || data.Locked || !_nodeViews.TryGetValue(pair.Key, out var view))
                        continue;

                    data.SetPosition(pair.Value);
                    var rect = view.GetPosition();
                    rect.position = pair.Value;
                    view.SetPosition(rect);
                }
            }
            finally
            {
                _suppressChanges = false;
            }

            EditorUtility.SetDirty(_authoring);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var ports = new List<Port>();
            foreach (var port in this.ports)
            {
                if (port == startPort ||
                    port.direction == startPort.direction ||
                    port.node == startPort.node)
                {
                    continue;
                }
                ports.Add(port);
            }
            return ports;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent eventData)
        {
            if (_authoring != null)
            {
                eventData.menu.AppendAction(
                    "Add PointAllocation Node",
                    _ => AddNodeAt(_lastContentMousePosition));
                eventData.menu.AppendSeparator();
            }
            base.BuildContextualMenu(eventData);
        }

        private PointAllocationNodeView AddNodeView(PointAllocationNodeAuthoringData data)
        {
            var view = new PointAllocationNodeView(data);
            _nodeViews.Add(data.Id, view);
            AddElement(view);
            return view;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_suppressChanges || _authoring == null)
                return change;

            if (change.movedElements != null && change.movedElements.Count > 0)
            {
                Undo.RecordObject(_authoring, "Move PointAllocation Nodes");
                for (var index = 0; index < change.movedElements.Count; index++)
                {
                    if (change.movedElements[index] is PointAllocationNodeView nodeView)
                        nodeView.Data.SetPosition(nodeView.GetPosition().position);
                }
                EditorUtility.SetDirty(_authoring);
            }

            if (change.edgesToCreate != null)
            {
                for (var index = change.edgesToCreate.Count - 1; index >= 0; index--)
                {
                    var edge = change.edgesToCreate[index];
                    if (!(edge.output?.node is PointAllocationNodeView from) ||
                        !(edge.input?.node is PointAllocationNodeView to))
                    {
                        change.edgesToCreate.RemoveAt(index);
                        continue;
                    }

                    Undo.RecordObject(_authoring, "Add PointAllocation Connection");
                    if (!_authoring.TryAddConnection(from.Data.Id, to.Data.Id, out var connection))
                    {
                        change.edgesToCreate.RemoveAt(index);
                        _window.ShowNotification(new GUIContent("连接重复、无效或会形成有向环。"));
                        continue;
                    }

                    edge.userData = connection;
                    EditorUtility.SetDirty(_authoring);
                }
            }

            if (change.elementsToRemove != null && change.elementsToRemove.Count > 0)
            {
                Undo.RecordObject(_authoring, "Delete PointAllocation Graph Elements");
                for (var index = 0; index < change.elementsToRemove.Count; index++)
                {
                    switch (change.elementsToRemove[index])
                    {
                        case PointAllocationNodeView nodeView:
                            _authoring.RemoveNode(nodeView.Data.Id);
                            _nodeViews.Remove(nodeView.Data.Id);
                            break;
                        case Edge edge when edge.userData is PointAllocationConnectionAuthoringData connection:
                            _authoring.RemoveConnection(connection.FromNodeId, connection.ToNodeId);
                            break;
                    }
                }
                EditorUtility.SetDirty(_authoring);
            }

            return change;
        }

        private void OnViewTransformChanged(GraphView graphView)
        {
            if (_suppressChanges || _authoring == null)
                return;

            _authoring.SetViewTransform(viewTransform.position, viewTransform.scale);
            EditorUtility.SetDirty(_authoring);
        }
    }
}
