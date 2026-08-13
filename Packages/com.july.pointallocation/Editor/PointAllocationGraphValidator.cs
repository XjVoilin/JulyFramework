using System;
using System.Collections.Generic;

namespace July.PointAllocation.Editor
{
    internal static class PointAllocationGraphValidator
    {
        internal static void Validate(PointAllocationGraph graph)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph), "加点图 graph 不能为 null。");
            if (graph.GraphId <= 0)
                throw new ArgumentException("GraphId 必须是正整数。");
            if (graph.Nodes == null || graph.Nodes.Length == 0)
                throw new ArgumentException("加点图至少需要包含一个节点。");
            if (graph.Connections == null)
                throw new ArgumentException("连接列表 Connections 不能为 null。");

            var nodes = new Dictionary<int, PointAllocationNode>(graph.Nodes.Length);
            for (var index = 0; index < graph.Nodes.Length; index++)
            {
                var node = graph.Nodes[index]
                    ?? throw new ArgumentException($"节点列表 Nodes 的第 {index} 项为 null。");
                ValidateNode(node);
                if (!nodes.TryAdd(node.Id, node))
                    throw new ArgumentException($"NodeId {node.Id} 重复。");
            }

            var connectionKeys = new HashSet<long>();
            var indegree = new Dictionary<int, int>(nodes.Count);
            var outgoing = new Dictionary<int, List<int>>(nodes.Count);
            foreach (var nodeId in nodes.Keys)
            {
                indegree.Add(nodeId, 0);
                outgoing.Add(nodeId, new List<int>());
            }

            for (var index = 0; index < graph.Connections.Length; index++)
            {
                var connection = graph.Connections[index]
                    ?? throw new ArgumentException($"连接列表 Connections 的第 {index} 项为 null。");
                if (!nodes.TryGetValue(connection.FromNodeId, out var source) ||
                    !nodes.ContainsKey(connection.ToNodeId))
                {
                    throw new ArgumentException(
                        $"连接 {connection.FromNodeId}->{connection.ToNodeId} 引用了不存在的节点。");
                }
                if (connection.FromNodeId == connection.ToNodeId)
                    throw new ArgumentException($"节点 {connection.FromNodeId} 不能连接到自身。");
                if (connection.RequiredLevel < 1 || connection.RequiredLevel > source.MaxLevel)
                {
                    throw new ArgumentException(
                        $"连接 {connection.FromNodeId}->{connection.ToNodeId} 的 RequiredLevel " +
                        $"必须在 1 到 {source.MaxLevel} 之间。");
                }

                var key = ((long)(uint)connection.FromNodeId << 32) |
                          (uint)connection.ToNodeId;
                if (!connectionKeys.Add(key))
                {
                    throw new ArgumentException(
                        $"连接 {connection.FromNodeId}->{connection.ToNodeId} 重复。");
                }

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
                    if (--indegree[targetId] == 0)
                        queue.Enqueue(targetId);
                }
            }
            if (visited != nodes.Count)
                throw new ArgumentException("加点图的连接中存在有向环。");
        }

        private static void ValidateNode(PointAllocationNode node)
        {
            if (node.Id <= 0)
                throw new ArgumentException($"NodeId {node.Id} 必须是正整数。");
            if (node.MaxLevel < 1)
                throw new ArgumentException($"节点 {node.Id} 的 MaxLevel 不能小于 1。");
            if (node.UpgradeCosts == null || node.UpgradeCosts.Length != node.MaxLevel)
            {
                throw new ArgumentException(
                    $"节点 {node.Id} 的升级消耗数量必须与 MaxLevel 完全一致。");
            }
            for (var level = 0; level < node.UpgradeCosts.Length; level++)
            {
                if (node.UpgradeCosts[level] < 0)
                {
                    throw new ArgumentException(
                        $"节点 {node.Id} 在等级 {level} 的升级消耗不能为负数。");
                }
            }
        }
    }
}
