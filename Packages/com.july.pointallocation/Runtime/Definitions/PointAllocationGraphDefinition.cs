using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace July.PointAllocation
{
    /// <summary>经过完整验证、可在多个运行时对象之间安全共享的不可变加点图定义。</summary>
    public sealed class PointAllocationGraphDefinition
    {
        private readonly PointAllocationNodeDefinition[] _nodes;
        private readonly PointAllocationConnectionDefinition[] _connections;
        private readonly ReadOnlyCollection<PointAllocationNodeDefinition> _nodesView;
        private readonly ReadOnlyCollection<PointAllocationConnectionDefinition> _connectionsView;
        private readonly Dictionary<int, PointAllocationNodeDefinition> _nodeById;
        private readonly Dictionary<int, PointAllocationConnectionDefinition[]> _incomingByNodeId;
        private readonly Dictionary<int, PointAllocationConnectionDefinition[]> _outgoingByNodeId;

        public int Id { get; }
        public IReadOnlyList<PointAllocationNodeDefinition> Nodes => _nodesView;
        public IReadOnlyList<PointAllocationConnectionDefinition> Connections => _connectionsView;

        private PointAllocationGraphDefinition(
            int id,
            PointAllocationNodeDefinition[] nodes,
            PointAllocationConnectionDefinition[] connections)
        {
            Id = id;
            _nodes = nodes;
            _connections = connections;
            _nodesView = Array.AsReadOnly(_nodes);
            _connectionsView = Array.AsReadOnly(_connections);
            _nodeById = new Dictionary<int, PointAllocationNodeDefinition>(_nodes.Length);

            var incoming = new Dictionary<int, List<PointAllocationConnectionDefinition>>(_nodes.Length);
            var outgoing = new Dictionary<int, List<PointAllocationConnectionDefinition>>(_nodes.Length);
            for (var index = 0; index < _nodes.Length; index++)
            {
                var node = _nodes[index];
                _nodeById.Add(node.Id, node);
                incoming.Add(node.Id, new List<PointAllocationConnectionDefinition>());
                outgoing.Add(node.Id, new List<PointAllocationConnectionDefinition>());
            }

            for (var index = 0; index < _connections.Length; index++)
            {
                var connection = _connections[index];
                incoming[connection.ToNodeId].Add(connection);
                outgoing[connection.FromNodeId].Add(connection);
            }

            _incomingByNodeId = FreezeConnectionIndex(incoming);
            _outgoingByNodeId = FreezeConnectionIndex(outgoing);
        }

        public bool TryGetNode(int nodeId, out PointAllocationNodeDefinition node) =>
            _nodeById.TryGetValue(nodeId, out node);

        internal IReadOnlyList<PointAllocationConnectionDefinition> GetIncomingConnections(int nodeId) =>
            _incomingByNodeId.TryGetValue(nodeId, out var connections)
                ? connections
                : Array.Empty<PointAllocationConnectionDefinition>();

        internal IReadOnlyList<PointAllocationConnectionDefinition> GetOutgoingConnections(int nodeId) =>
            _outgoingByNodeId.TryGetValue(nodeId, out var connections)
                ? connections
                : Array.Empty<PointAllocationConnectionDefinition>();

        public static bool TryCreate(
            int definitionId,
            IReadOnlyList<PointAllocationNodeDefinition> nodes,
            IReadOnlyList<PointAllocationConnectionDefinition> connections,
            out PointAllocationGraphDefinition definition,
            out IReadOnlyList<PointAllocationDefinitionError> errors)
        {
            var validationErrors = Validate(definitionId, nodes, connections);
            errors = validationErrors;
            if (validationErrors.Count > 0)
            {
                definition = null;
                return false;
            }

            var nodeSnapshot = new PointAllocationNodeDefinition[nodes.Count];
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                nodeSnapshot[index] = new PointAllocationNodeDefinition(
                    node.Id,
                    node.MaxRank,
                    node.RankCosts);
            }

            Array.Sort(nodeSnapshot, (left, right) => left.Id.CompareTo(right.Id));

            var connectionSnapshot = new PointAllocationConnectionDefinition[connections.Count];
            for (var index = 0; index < connections.Count; index++)
            {
                var connection = connections[index];
                connectionSnapshot[index] = new PointAllocationConnectionDefinition(
                    connection.FromNodeId,
                    connection.ToNodeId,
                    connection.RequiredRank);
            }

            Array.Sort(connectionSnapshot, CompareConnections);
            definition = new PointAllocationGraphDefinition(definitionId, nodeSnapshot, connectionSnapshot);
            return true;
        }

        public static IReadOnlyList<PointAllocationDefinitionError> Validate(
            int definitionId,
            IReadOnlyList<PointAllocationNodeDefinition> nodes,
            IReadOnlyList<PointAllocationConnectionDefinition> connections)
        {
            var errors = new List<PointAllocationDefinitionError>();
            if (definitionId <= 0)
            {
                errors.Add(new PointAllocationDefinitionError(
                    PointAllocationDefinitionErrorCode.InvalidDefinitionId,
                    "DefinitionId must be a positive integer."));
            }

            if (nodes == null)
            {
                errors.Add(new PointAllocationDefinitionError(
                    PointAllocationDefinitionErrorCode.MissingNodes,
                    "Nodes cannot be null."));
            }
            else if (nodes.Count == 0)
            {
                errors.Add(new PointAllocationDefinitionError(
                    PointAllocationDefinitionErrorCode.EmptyNodes,
                    "A point-allocation graph must contain at least one node."));
            }

            if (connections == null)
            {
                errors.Add(new PointAllocationDefinitionError(
                    PointAllocationDefinitionErrorCode.MissingConnections,
                    "Connections cannot be null."));
            }

            if (nodes == null || connections == null)
                return errors.AsReadOnly();

            var nodeById = new Dictionary<int, PointAllocationNodeDefinition>();
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                if (node == null)
                {
                    errors.Add(new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.NullNode,
                        $"Node at index {index} is null."));
                    continue;
                }

                if (node.Id <= 0)
                {
                    errors.Add(new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.InvalidNodeId,
                        $"NodeId {node.Id} must be a positive integer.",
                        node.Id));
                }
                else if (!nodeById.TryAdd(node.Id, node))
                {
                    errors.Add(new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.DuplicateNodeId,
                        $"NodeId {node.Id} is duplicated.",
                        node.Id));
                }

                if (node.MaxRank < 1)
                {
                    errors.Add(new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.InvalidMaxRank,
                        $"Node {node.Id} MaxRank must be at least 1.",
                        node.Id));
                }

                if (node.RankCosts == null || node.RankCosts.Count != node.MaxRank)
                {
                    errors.Add(new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.InvalidRankCosts,
                        $"Node {node.Id} must contain exactly MaxRank rank costs.",
                        node.Id));
                }
                else
                {
                    for (var rankIndex = 0; rankIndex < node.RankCosts.Count; rankIndex++)
                    {
                        if (node.RankCosts[rankIndex] > 0)
                            continue;

                        errors.Add(new PointAllocationDefinitionError(
                            PointAllocationDefinitionErrorCode.InvalidRankCost,
                            $"Node {node.Id} rank cost at index {rankIndex} must be positive.",
                            node.Id));
                    }
                }
            }

            var connectionKeys = new HashSet<long>();
            var validConnections = new List<PointAllocationConnectionDefinition>(connections.Count);
            for (var index = 0; index < connections.Count; index++)
            {
                var connection = connections[index];
                if (connection == null)
                {
                    errors.Add(new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.NullConnection,
                        $"Connection at index {index} is null."));
                    continue;
                }

                var fromExists = nodeById.TryGetValue(connection.FromNodeId, out var fromNode);
                var toExists = nodeById.ContainsKey(connection.ToNodeId);
                if (!fromExists || !toExists)
                {
                    errors.Add(new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.UnknownConnectionNode,
                        $"Connection {connection.FromNodeId}->{connection.ToNodeId} references an unknown node.",
                        connection.FromNodeId,
                        connection.ToNodeId));
                    continue;
                }

                if (connection.FromNodeId == connection.ToNodeId)
                {
                    errors.Add(new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.SelfConnection,
                        $"Node {connection.FromNodeId} cannot connect to itself.",
                        connection.FromNodeId));
                    continue;
                }

                var key = ((long)(uint)connection.FromNodeId << 32) |
                          (uint)connection.ToNodeId;
                if (!connectionKeys.Add(key))
                {
                    errors.Add(new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.DuplicateConnection,
                        $"Connection {connection.FromNodeId}->{connection.ToNodeId} is duplicated.",
                        connection.FromNodeId,
                        connection.ToNodeId));
                    continue;
                }

                if (connection.RequiredRank < 1 || connection.RequiredRank > fromNode.MaxRank)
                {
                    errors.Add(new PointAllocationDefinitionError(
                        PointAllocationDefinitionErrorCode.InvalidRequiredRank,
                        $"Connection {connection.FromNodeId}->{connection.ToNodeId} RequiredRank must be between 1 and {fromNode.MaxRank}.",
                        connection.FromNodeId,
                        connection.ToNodeId));
                    continue;
                }

                validConnections.Add(connection);
            }

            if (HasDirectedCycle(nodeById.Keys, validConnections))
            {
                errors.Add(new PointAllocationDefinitionError(
                    PointAllocationDefinitionErrorCode.DirectedCycle,
                    "PointAllocation connections contain a directed cycle."));
            }

            return errors.AsReadOnly();
        }

        private static bool HasDirectedCycle(
            Dictionary<int, PointAllocationNodeDefinition>.KeyCollection nodeIds,
            IReadOnlyList<PointAllocationConnectionDefinition> connections)
        {
            var indegree = new Dictionary<int, int>();
            var outgoing = new Dictionary<int, List<int>>();
            foreach (var nodeId in nodeIds)
            {
                indegree.Add(nodeId, 0);
                outgoing.Add(nodeId, new List<int>());
            }

            for (var index = 0; index < connections.Count; index++)
            {
                var connection = connections[index];
                outgoing[connection.FromNodeId].Add(connection.ToNodeId);
                indegree[connection.ToNodeId]++;
            }

            var queue = new Queue<int>();
            foreach (var pair in indegree)
                if (pair.Value == 0) queue.Enqueue(pair.Key);

            var visited = 0;
            while (queue.Count > 0)
            {
                var nodeId = queue.Dequeue();
                visited++;
                var targets = outgoing[nodeId];
                for (var index = 0; index < targets.Count; index++)
                {
                    var targetId = targets[index];
                    indegree[targetId]--;
                    if (indegree[targetId] == 0)
                        queue.Enqueue(targetId);
                }
            }

            return visited != indegree.Count;
        }

        private static int CompareConnections(
            PointAllocationConnectionDefinition left,
            PointAllocationConnectionDefinition right)
        {
            var from = left.FromNodeId.CompareTo(right.FromNodeId);
            if (from != 0) return from;
            return left.ToNodeId.CompareTo(right.ToNodeId);
        }

        private static Dictionary<int, PointAllocationConnectionDefinition[]> FreezeConnectionIndex(
            Dictionary<int, List<PointAllocationConnectionDefinition>> source)
        {
            var result = new Dictionary<int, PointAllocationConnectionDefinition[]>(source.Count);
            foreach (var pair in source)
            {
                pair.Value.Sort(CompareConnections);
                result.Add(pair.Key, pair.Value.ToArray());
            }

            return result;
        }
    }
}
