using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace July.PointAllocation.Editor
{
    [Serializable]
    internal sealed class PointAllocationNodeEditorData
    {
        [SerializeField] private int _nodeId;
        [SerializeField] private string _label;
        [SerializeField, TextArea] private string _note;
        [SerializeField] private Vector2 _position;
        [SerializeField] private bool _locked;

        public int NodeId => _nodeId;
        public string Label => _label;
        public string Note => _note;
        public Vector2 Position => _position;
        public bool Locked => _locked;

        internal PointAllocationNodeEditorData(int nodeId, Vector2 position)
        {
            _nodeId = nodeId;
            _position = position;
            _label = $"Node {nodeId}";
            _note = string.Empty;
        }

        internal void SetLabel(string value) => _label = value ?? string.Empty;
        internal void SetNote(string value) => _note = value ?? string.Empty;
        internal void SetPosition(Vector2 value) => _position = value;
        internal void SetLocked(bool value) => _locked = value;
    }

    /// <summary>JSON 的可选伴生工作区，只持有 Editor 元数据。</summary>
    public sealed class PointAllocationEditorWorkspace : ScriptableObject
    {
        [SerializeField] private TextAsset _graphJson;
        [SerializeField] private List<PointAllocationNodeEditorData> _nodes =
            new List<PointAllocationNodeEditorData>();
        [SerializeField] private int _nextNodeId = 1;
        [SerializeField] private Vector3 _viewPosition;
        [SerializeField] private Vector3 _viewScale = Vector3.one;

        internal TextAsset GraphJson => _graphJson;
        internal IReadOnlyList<PointAllocationNodeEditorData> Nodes => _nodes;
        internal int NextNodeId => _nextNodeId;
        internal Vector3 ViewPosition => _viewPosition;
        internal Vector3 ViewScale => _viewScale;

        internal void SetGraphJson(TextAsset value) => _graphJson = value;

        internal PointAllocationNodeEditorData FindNode(int nodeId) =>
            _nodes.FirstOrDefault(node => node != null && node.NodeId == nodeId);

        internal PointAllocationNodeEditorData GetOrCreateNode(int nodeId, Vector2 defaultPosition)
        {
            Normalize();
            var existing = FindNode(nodeId);
            if (existing != null)
                return existing;

            var created = new PointAllocationNodeEditorData(nodeId, defaultPosition);
            _nodes.Add(created);
            _nextNodeId = Mathf.Max(_nextNodeId, nodeId + 1);
            return created;
        }

        internal int AllocateNodeId()
        {
            Normalize();
            return _nextNodeId++;
        }

        internal void RemoveNode(int nodeId) =>
            _nodes.RemoveAll(node => node != null && node.NodeId == nodeId);

        internal void Synchronize(IEnumerable<int> nodeIds)
        {
            Normalize();
            var ids = new HashSet<int>(nodeIds);
            _nodes.RemoveAll(node => node == null || !ids.Contains(node.NodeId));
            var index = 0;
            foreach (var nodeId in ids.OrderBy(value => value))
            {
                GetOrCreateNode(nodeId, new Vector2(index * 260f, 0f));
                index++;
            }
        }

        internal void SetViewTransform(Vector3 position, Vector3 scale)
        {
            _viewPosition = position;
            _viewScale = scale;
        }

        internal void Normalize()
        {
            _nodes ??= new List<PointAllocationNodeEditorData>();
            _nodes.RemoveAll(node => node == null);
            var maxNodeId = 0;
            for (var index = 0; index < _nodes.Count; index++)
                maxNodeId = Mathf.Max(maxNodeId, _nodes[index].NodeId);
            _nextNodeId = Mathf.Max(_nextNodeId, maxNodeId + 1, 1);
            if (_viewScale == Vector3.zero)
                _viewScale = Vector3.one;
        }

        private void OnValidate() => Normalize();
    }

    internal sealed class PointAllocationEditableNode
    {
        private readonly List<int> _upgradeCosts;

        public int Id { get; }
        public int MaxLevel { get; private set; }
        public IReadOnlyList<int> UpgradeCosts => _upgradeCosts;
        public PointAllocationNodeEditorData EditorData { get; }

        internal PointAllocationEditableNode(
            PointAllocationNode graph,
            PointAllocationNodeEditorData editorData)
        {
            Id = graph.Id;
            MaxLevel = graph.MaxLevel;
            _upgradeCosts = graph.UpgradeCosts.ToList();
            EditorData = editorData;
        }

        internal PointAllocationEditableNode(int id, PointAllocationNodeEditorData editorData)
        {
            Id = id;
            MaxLevel = 1;
            _upgradeCosts = new List<int> { 1 };
            EditorData = editorData;
        }

        internal void SetMaxLevel(int value)
        {
            MaxLevel = Mathf.Max(1, value);
            while (_upgradeCosts.Count < MaxLevel)
                _upgradeCosts.Add(_upgradeCosts.Count == 0 ? 1 : _upgradeCosts[^1]);
            while (_upgradeCosts.Count > MaxLevel)
                _upgradeCosts.RemoveAt(_upgradeCosts.Count - 1);
        }

        internal void SetUpgradeCost(int level, int value)
        {
            if (level >= 0 && level < _upgradeCosts.Count)
                _upgradeCosts[level] = Mathf.Max(0, value);
        }

        internal PointAllocationNode ToGraph() =>
            new PointAllocationNode(Id, MaxLevel, _upgradeCosts.ToArray());
    }

    internal sealed class PointAllocationEditableConnection
    {
        public int FromNodeId { get; }
        public int ToNodeId { get; }
        public int RequiredLevel { get; private set; }

        internal PointAllocationEditableConnection(int fromNodeId, int toNodeId, int requiredLevel)
        {
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            RequiredLevel = requiredLevel;
        }

        internal void SetRequiredLevel(int value, int sourceMaxLevel) =>
            RequiredLevel = Mathf.Clamp(value, 1, Mathf.Max(1, sourceMaxLevel));

        internal PointAllocationConnection ToGraph() =>
            new PointAllocationConnection(FromNodeId, ToNodeId, RequiredLevel);
    }

    internal sealed class PointAllocationEditorDocument
    {
        private readonly List<PointAllocationEditableNode> _nodes =
            new List<PointAllocationEditableNode>();
        private readonly List<PointAllocationEditableConnection> _connections =
            new List<PointAllocationEditableConnection>();

        public int GraphId { get; private set; }
        public IReadOnlyList<PointAllocationEditableNode> Nodes => _nodes;
        public IReadOnlyList<PointAllocationEditableConnection> Connections => _connections;
        public PointAllocationEditorWorkspace Workspace { get; }

        internal PointAllocationEditorDocument(
            PointAllocationGraph graph,
            PointAllocationEditorWorkspace workspace)
        {
            GraphId = graph.GraphId;
            Workspace = workspace ?? throw new ArgumentNullException(
                nameof(workspace),
                "编辑器工作区 workspace 不能为 null。");
            Workspace.Synchronize(graph.Nodes.Select(node => node.Id));

            for (var index = 0; index < graph.Nodes.Length; index++)
            {
                var node = graph.Nodes[index];
                _nodes.Add(new PointAllocationEditableNode(
                    node,
                    Workspace.FindNode(node.Id)));
            }
            for (var index = 0; index < graph.Connections.Length; index++)
            {
                var connection = graph.Connections[index];
                _connections.Add(new PointAllocationEditableConnection(
                    connection.FromNodeId,
                    connection.ToNodeId,
                    connection.RequiredLevel));
            }
        }

        internal void SetGraphId(int value) => GraphId = value;

        internal PointAllocationEditableNode AddNode(Vector2 position)
        {
            var id = Workspace.AllocateNodeId();
            var node = new PointAllocationEditableNode(
                id,
                Workspace.GetOrCreateNode(id, position));
            _nodes.Add(node);
            return node;
        }

        internal bool RemoveNode(int nodeId)
        {
            if (_nodes.Count <= 1)
                return false;
            var removed = _nodes.RemoveAll(node => node.Id == nodeId) > 0;
            if (!removed)
                return false;
            _connections.RemoveAll(connection =>
                connection.FromNodeId == nodeId || connection.ToNodeId == nodeId);
            Workspace.RemoveNode(nodeId);
            return true;
        }

        internal PointAllocationEditableNode FindNode(int nodeId) =>
            _nodes.FirstOrDefault(node => node.Id == nodeId);

        internal PointAllocationEditableConnection FindConnection(int fromNodeId, int toNodeId) =>
            _connections.FirstOrDefault(connection =>
                connection.FromNodeId == fromNodeId && connection.ToNodeId == toNodeId);

        internal bool TryAddConnection(
            int fromNodeId,
            int toNodeId,
            out PointAllocationEditableConnection connection)
        {
            connection = null;
            if (fromNodeId == toNodeId ||
                FindNode(fromNodeId) == null ||
                FindNode(toNodeId) == null ||
                FindConnection(fromNodeId, toNodeId) != null ||
                WouldCreateCycle(fromNodeId, toNodeId))
            {
                return false;
            }

            connection = new PointAllocationEditableConnection(fromNodeId, toNodeId, 1);
            _connections.Add(connection);
            return true;
        }

        internal void RemoveConnection(int fromNodeId, int toNodeId) =>
            _connections.RemoveAll(connection =>
                connection.FromNodeId == fromNodeId && connection.ToNodeId == toNodeId);

        internal void SetNodeMaxLevel(PointAllocationEditableNode node, int maxLevel)
        {
            node.SetMaxLevel(maxLevel);
            for (var index = 0; index < _connections.Count; index++)
            {
                var connection = _connections[index];
                if (connection.FromNodeId == node.Id)
                    connection.SetRequiredLevel(connection.RequiredLevel, node.MaxLevel);
            }
        }

        internal PointAllocationGraph CreateGraph() =>
            new PointAllocationGraph(
                GraphId,
                _nodes.OrderBy(node => node.Id).Select(node => node.ToGraph()).ToArray(),
                _connections
                    .OrderBy(connection => connection.FromNodeId)
                    .ThenBy(connection => connection.ToNodeId)
                    .Select(connection => connection.ToGraph())
                    .ToArray());

        private bool WouldCreateCycle(int fromNodeId, int toNodeId)
        {
            var outgoing = _nodes.ToDictionary(node => node.Id, _ => new List<int>());
            for (var index = 0; index < _connections.Count; index++)
                outgoing[_connections[index].FromNodeId].Add(_connections[index].ToNodeId);
            outgoing[fromNodeId].Add(toNodeId);

            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(toNodeId);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == fromNodeId)
                    return true;
                if (!visited.Add(current))
                    continue;
                for (var index = 0; index < outgoing[current].Count; index++)
                    stack.Push(outgoing[current][index]);
            }
            return false;
        }
    }
}
