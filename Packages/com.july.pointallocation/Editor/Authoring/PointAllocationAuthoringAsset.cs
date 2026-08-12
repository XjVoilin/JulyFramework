using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace July.PointAllocation.Editor
{
    [Serializable]
    public sealed class PointAllocationNodeAuthoringData
    {
        [SerializeField] private int _id;
        [SerializeField] private int _maxRank = 1;
        [SerializeField] private List<int> _rankCosts = new List<int> { 1 };
        [SerializeField] private string _label;
        [SerializeField, TextArea] private string _note;
        [SerializeField] private Vector2 _position;
        [SerializeField] private bool _locked;

        public int Id => _id;
        public int MaxRank => _maxRank;
        public IReadOnlyList<int> RankCosts => _rankCosts;
        public string Label => _label;
        public string Note => _note;
        public Vector2 Position => _position;
        public bool Locked => _locked;

        internal PointAllocationNodeAuthoringData(int id, Vector2 position)
        {
            _id = id;
            _position = position;
            _label = $"Node {id}";
        }

        internal void SetPosition(Vector2 position) => _position = position;
        internal void SetLabel(string label) => _label = label ?? string.Empty;
        internal void SetNote(string note) => _note = note ?? string.Empty;
        internal void SetLocked(bool locked) => _locked = locked;

        internal void SetMaxRank(int maxRank)
        {
            _maxRank = Mathf.Max(1, maxRank);
            _rankCosts ??= new List<int>();
            while (_rankCosts.Count < _maxRank)
                _rankCosts.Add(_rankCosts.Count == 0 ? 1 : Mathf.Max(1, _rankCosts[^1]));
            while (_rankCosts.Count > _maxRank)
                _rankCosts.RemoveAt(_rankCosts.Count - 1);
            NormalizeCosts();
        }

        internal void SetRankCost(int rankIndex, int cost)
        {
            SetMaxRank(_maxRank);
            if (rankIndex < 0 || rankIndex >= _rankCosts.Count)
                return;
            _rankCosts[rankIndex] = Mathf.Max(1, cost);
        }

        internal void Normalize()
        {
            SetMaxRank(_maxRank);
            _label ??= string.Empty;
            _note ??= string.Empty;
        }

        internal PointAllocationNodeDefinition ToDefinition()
        {
            Normalize();
            return new PointAllocationNodeDefinition(_id, _maxRank, _rankCosts);
        }

        private void NormalizeCosts()
        {
            for (var index = 0; index < _rankCosts.Count; index++)
                _rankCosts[index] = Mathf.Max(1, _rankCosts[index]);
        }
    }

    [Serializable]
    public sealed class PointAllocationConnectionAuthoringData
    {
        [SerializeField] private int _fromNodeId;
        [SerializeField] private int _toNodeId;
        [SerializeField] private int _requiredRank = 1;

        public int FromNodeId => _fromNodeId;
        public int ToNodeId => _toNodeId;
        public int RequiredRank => _requiredRank;

        internal PointAllocationConnectionAuthoringData(int fromNodeId, int toNodeId, int requiredRank)
        {
            _fromNodeId = fromNodeId;
            _toNodeId = toNodeId;
            _requiredRank = requiredRank;
        }

        internal void SetRequiredRank(int requiredRank, int sourceMaxRank)
        {
            _requiredRank = Mathf.Clamp(requiredRank, 1, Mathf.Max(1, sourceMaxRank));
        }

        internal PointAllocationConnectionDefinition ToDefinition() =>
            new PointAllocationConnectionDefinition(_fromNodeId, _toNodeId, _requiredRank);
    }

    /// <summary>仅供加点图编辑器使用的逻辑图源资产。</summary>
    [CreateAssetMenu(
        fileName = "PointAllocationAuthoring",
        menuName = "JulyGF/加点图/编辑源",
        order = 0)]
    public sealed class PointAllocationAuthoringAsset : ScriptableObject
    {
        [FormerlySerializedAs("_treeId")]
        [SerializeField] private int _definitionId = 1;
        [SerializeField] private List<PointAllocationNodeAuthoringData> _nodes = new List<PointAllocationNodeAuthoringData>();
        [SerializeField] private List<PointAllocationConnectionAuthoringData> _connections = new List<PointAllocationConnectionAuthoringData>();
        [SerializeField] private int _nextNodeId = 1;
        [SerializeField] private Vector3 _viewPosition;
        [SerializeField] private Vector3 _viewScale = Vector3.one;
        [SerializeField] private PointAllocationGraphDefinitionAsset _runtimeAsset;

        public int DefinitionId => _definitionId;
        public IReadOnlyList<PointAllocationNodeAuthoringData> Nodes => _nodes;
        public IReadOnlyList<PointAllocationConnectionAuthoringData> Connections => _connections;
        public int NextNodeId => _nextNodeId;
        public Vector3 ViewPosition => _viewPosition;
        public Vector3 ViewScale => _viewScale;
        public PointAllocationGraphDefinitionAsset RuntimeAsset => _runtimeAsset;

        internal void SetDefinitionId(int definitionId) => _definitionId = definitionId;
        internal void SetRuntimeAsset(PointAllocationGraphDefinitionAsset runtimeAsset) => _runtimeAsset = runtimeAsset;

        internal PointAllocationNodeAuthoringData AddNode(Vector2 position)
        {
            Normalize();
            var node = new PointAllocationNodeAuthoringData(_nextNodeId++, position);
            _nodes.Add(node);
            return node;
        }

        internal bool RemoveNode(int nodeId)
        {
            var removed = _nodes.RemoveAll(node => node != null && node.Id == nodeId) > 0;
            if (removed)
            {
                _connections.RemoveAll(connection =>
                    connection != null &&
                    (connection.FromNodeId == nodeId || connection.ToNodeId == nodeId));
            }

            return removed;
        }

        internal bool TryAddConnection(int fromNodeId, int toNodeId, out PointAllocationConnectionAuthoringData connection)
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

            connection = new PointAllocationConnectionAuthoringData(fromNodeId, toNodeId, 1);
            _connections.Add(connection);
            return true;
        }

        internal bool RemoveConnection(int fromNodeId, int toNodeId) =>
            _connections.RemoveAll(connection =>
                connection != null &&
                connection.FromNodeId == fromNodeId &&
                connection.ToNodeId == toNodeId) > 0;

        internal PointAllocationNodeAuthoringData FindNode(int nodeId) =>
            _nodes.FirstOrDefault(node => node != null && node.Id == nodeId);

        internal PointAllocationConnectionAuthoringData FindConnection(int fromNodeId, int toNodeId) =>
            _connections.FirstOrDefault(connection =>
                connection != null &&
                connection.FromNodeId == fromNodeId &&
                connection.ToNodeId == toNodeId);

        internal void SetViewTransform(Vector3 position, Vector3 scale)
        {
            _viewPosition = position;
            _viewScale = scale;
        }

        internal void SetNodeMaxRank(PointAllocationNodeAuthoringData node, int maxRank)
        {
            if (node == null)
                return;

            node.SetMaxRank(maxRank);
            for (var index = 0; index < _connections.Count; index++)
            {
                var connection = _connections[index];
                if (connection != null && connection.FromNodeId == node.Id)
                    connection.SetRequiredRank(connection.RequiredRank, node.MaxRank);
            }
        }

        internal void Normalize()
        {
            _nodes ??= new List<PointAllocationNodeAuthoringData>();
            _connections ??= new List<PointAllocationConnectionAuthoringData>();
            _nodes.RemoveAll(node => node == null);
            _connections.RemoveAll(connection => connection == null);

            var maxNodeId = 0;
            for (var index = 0; index < _nodes.Count; index++)
            {
                _nodes[index].Normalize();
                maxNodeId = Mathf.Max(maxNodeId, _nodes[index].Id);
            }

            _nextNodeId = Mathf.Max(_nextNodeId, maxNodeId + 1, 1);
            if (_viewScale == Vector3.zero)
                _viewScale = Vector3.one;
        }

        internal PointAllocationNodeDefinition[] CreateNodeDefinitions()
        {
            Normalize();
            return _nodes
                .OrderBy(node => node.Id)
                .Select(node => node.ToDefinition())
                .ToArray();
        }

        internal PointAllocationConnectionDefinition[] CreateConnectionDefinitions()
        {
            Normalize();
            return _connections
                .OrderBy(connection => connection.FromNodeId)
                .ThenBy(connection => connection.ToNodeId)
                .Select(connection => connection.ToDefinition())
                .ToArray();
        }

        internal IReadOnlyList<PointAllocationDefinitionError> ValidateDefinition()
        {
            return PointAllocationGraphDefinition.Validate(
                _definitionId,
                CreateNodeDefinitions(),
                CreateConnectionDefinitions());
        }

        private bool WouldCreateCycle(int fromNodeId, int toNodeId)
        {
            var outgoing = new Dictionary<int, List<int>>();
            for (var index = 0; index < _nodes.Count; index++)
            {
                var node = _nodes[index];
                if (node != null)
                    outgoing[node.Id] = new List<int>();
            }

            for (var index = 0; index < _connections.Count; index++)
            {
                var existing = _connections[index];
                if (existing != null && outgoing.TryGetValue(existing.FromNodeId, out var targets))
                    targets.Add(existing.ToNodeId);
            }

            if (!outgoing.TryGetValue(fromNodeId, out var proposedTargets))
                return true;
            proposedTargets.Add(toNodeId);

            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(toNodeId);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == fromNodeId)
                    return true;
                if (!visited.Add(current) || !outgoing.TryGetValue(current, out var targets))
                    continue;
                for (var index = 0; index < targets.Count; index++)
                    stack.Push(targets[index]);
            }

            return false;
        }

        private void OnValidate()
        {
            Normalize();
        }
    }
}
