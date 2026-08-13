using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace July.PointAllocation.Editor
{
    internal enum PointAllocationLayoutDirection
    {
        TopToBottom = 0,
        LeftToRight = 1
    }

    internal readonly struct PointAllocationLayoutNode
    {
        public int Id { get; }
        public Vector2 Position { get; }

        internal PointAllocationLayoutNode(int id, Vector2 position)
        {
            Id = id;
            Position = position;
        }
    }

    internal readonly struct PointAllocationLayoutResult
    {
        public bool Success { get; }
        public string Error { get; }
        public IReadOnlyDictionary<int, Vector2> Positions { get; }

        internal PointAllocationLayoutResult(
            bool success,
            string error,
            IReadOnlyDictionary<int, Vector2> positions)
        {
            Success = success;
            Error = error;
            Positions = positions;
        }
    }

    /// <summary>稳定的分层 DAG 布局；方向只是同一算法的参数。</summary>
    internal static class PointAllocationLayeredLayout
    {
        internal static PointAllocationLayoutResult Calculate(
            IReadOnlyList<PointAllocationLayoutNode> nodes,
            IReadOnlyList<PointAllocationConnection> connections,
            PointAllocationLayoutDirection direction,
            float layerSpacing = 300f,
            float nodeSpacing = 190f)
        {
            if (nodes == null || connections == null)
                return Failed("节点和连接列表不能为 null。");
            if (nodes.Count == 0)
                return Succeeded(new Dictionary<int, Vector2>());

            layerSpacing = Mathf.Max(80f, layerSpacing);
            nodeSpacing = Mathf.Max(60f, nodeSpacing);

            var nodeById = new Dictionary<int, PointAllocationLayoutNode>();
            var indegree = new Dictionary<int, int>();
            var incoming = new Dictionary<int, List<int>>();
            var outgoing = new Dictionary<int, List<int>>();
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                if (!nodeById.TryAdd(node.Id, node))
                    return Failed($"Duplicate NodeId {node.Id}.");
                indegree.Add(node.Id, 0);
                incoming.Add(node.Id, new List<int>());
                outgoing.Add(node.Id, new List<int>());
            }

            for (var index = 0; index < connections.Count; index++)
            {
                var connection = connections[index];
                if (connection == null ||
                    !nodeById.ContainsKey(connection.FromNodeId) ||
                    !nodeById.ContainsKey(connection.ToNodeId))
                {
                    return Failed("连接引用了不存在的节点。");
                }

                outgoing[connection.FromNodeId].Add(connection.ToNodeId);
                incoming[connection.ToNodeId].Add(connection.FromNodeId);
                indegree[connection.ToNodeId]++;
            }

            var available = new SortedSet<int>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            var layerById = nodeById.Keys.ToDictionary(id => id, _ => 0);
            var topological = new List<int>(nodes.Count);
            while (available.Count > 0)
            {
                var nodeId = available.Min;
                available.Remove(nodeId);
                topological.Add(nodeId);

                var targets = outgoing[nodeId];
                targets.Sort();
                for (var index = 0; index < targets.Count; index++)
                {
                    var targetId = targets[index];
                    layerById[targetId] = Mathf.Max(layerById[targetId], layerById[nodeId] + 1);
                    indegree[targetId]--;
                    if (indegree[targetId] == 0)
                        available.Add(targetId);
                }
            }

            if (topological.Count != nodes.Count)
                return Failed("存在有向环的加点图无法自动布局。");

            var layers = new SortedDictionary<int, List<int>>();
            foreach (var nodeId in topological)
            {
                var layer = layerById[nodeId];
                if (!layers.TryGetValue(layer, out var values))
                {
                    values = new List<int>();
                    layers.Add(layer, values);
                }
                values.Add(nodeId);
            }

            foreach (var pair in layers)
            {
                pair.Value.Sort((left, right) =>
                {
                    var leftCross = GetCross(nodeById[left].Position, direction);
                    var rightCross = GetCross(nodeById[right].Position, direction);
                    var compare = leftCross.CompareTo(rightCross);
                    return compare != 0 ? compare : left.CompareTo(right);
                });
            }

            for (var pass = 0; pass < 4; pass++)
            {
                foreach (var pair in layers)
                {
                    if (pair.Key == 0) continue;
                    SortByBarycenter(pair.Value, incoming, layers[pair.Key - 1]);
                }

                foreach (var pair in layers.Reverse())
                {
                    if (!layers.ContainsKey(pair.Key + 1)) continue;
                    SortByBarycenter(pair.Value, outgoing, layers[pair.Key + 1]);
                }
            }

            var positions = new Dictionary<int, Vector2>(nodes.Count);
            foreach (var pair in layers)
            {
                var count = pair.Value.Count;
                var firstCross = -(count - 1) * nodeSpacing * 0.5f;
                for (var index = 0; index < count; index++)
                {
                    var main = pair.Key * layerSpacing;
                    var cross = firstCross + index * nodeSpacing;
                    positions[pair.Value[index]] = direction == PointAllocationLayoutDirection.TopToBottom
                        ? new Vector2(cross, main)
                        : new Vector2(main, cross);
                }
            }

            var currentCenter = Vector2.zero;
            var layoutCenter = Vector2.zero;
            for (var index = 0; index < nodes.Count; index++)
            {
                currentCenter += nodes[index].Position;
                layoutCenter += positions[nodes[index].Id];
            }
            currentCenter /= nodes.Count;
            layoutCenter /= nodes.Count;
            var offset = currentCenter - layoutCenter;

            var ids = positions.Keys.ToArray();
            for (var index = 0; index < ids.Length; index++)
                positions[ids[index]] += offset;

            return Succeeded(positions);
        }

        private static void SortByBarycenter(
            List<int> layer,
            IReadOnlyDictionary<int, List<int>> neighbors,
            IReadOnlyList<int> adjacentLayer)
        {
            var adjacentIndex = new Dictionary<int, int>(adjacentLayer.Count);
            for (var index = 0; index < adjacentLayer.Count; index++)
                adjacentIndex[adjacentLayer[index]] = index;

            var originalIndex = new Dictionary<int, int>(layer.Count);
            for (var index = 0; index < layer.Count; index++)
                originalIndex[layer[index]] = index;

            layer.Sort((left, right) =>
            {
                var leftValue = GetBarycenter(left, neighbors, adjacentIndex, originalIndex[left]);
                var rightValue = GetBarycenter(right, neighbors, adjacentIndex, originalIndex[right]);
                var compare = leftValue.CompareTo(rightValue);
                if (compare != 0) return compare;
                compare = originalIndex[left].CompareTo(originalIndex[right]);
                return compare != 0 ? compare : left.CompareTo(right);
            });
        }

        private static float GetBarycenter(
            int nodeId,
            IReadOnlyDictionary<int, List<int>> neighbors,
            IReadOnlyDictionary<int, int> adjacentIndex,
            int fallback)
        {
            if (!neighbors.TryGetValue(nodeId, out var values) || values.Count == 0)
                return fallback;

            var sum = 0f;
            var count = 0;
            for (var index = 0; index < values.Count; index++)
            {
                if (!adjacentIndex.TryGetValue(values[index], out var position))
                    continue;
                sum += position;
                count++;
            }

            return count == 0 ? fallback : sum / count;
        }

        private static float GetCross(Vector2 position, PointAllocationLayoutDirection direction) =>
            direction == PointAllocationLayoutDirection.TopToBottom ? position.x : position.y;

        private static PointAllocationLayoutResult Failed(string error) =>
            new PointAllocationLayoutResult(false, error, null);

        private static PointAllocationLayoutResult Succeeded(IReadOnlyDictionary<int, Vector2> positions) =>
            new PointAllocationLayoutResult(true, null, positions);
    }
}
