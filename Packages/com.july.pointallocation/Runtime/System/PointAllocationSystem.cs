using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using July.Arch;
using LitJson;

namespace July.PointAllocation
{
    public sealed class PointAllocationSystem : SystemBase, IPointAllocationSystem
    {
        private PointAllocationStore _store;

        public int AvailablePoints => _store.AvailablePoints;

        protected override UniTask OnInitializeAsync()
        {
            _store = GetStore<PointAllocationStore>()
                     ?? throw new InvalidOperationException("初始化 PointAllocationSystem 前必须先注册 PointAllocationStore。");

            return UniTask.CompletedTask;
        }

        public void LoadGraph(string json)
        {
            PointAllocationGraph graph;
            try
            {
                graph = JsonMapper.ToObject<PointAllocationGraph>(json);
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("加点图 JSON 格式无效。", nameof(json), exception);
            }

            _store.AddGraph(graph);
        }

        public void ReplaceState(int graphId, List<PointAllocationNodeState> nodeStates, int availablePoints)
        {
            var graph = GetGraphOrThrow(graphId);
            ValidateState(graph, nodeStates, availablePoints);

            _store.ReplaceGraphState(graphId, nodeStates, availablePoints);
            Publish(new PointAllocationChangedEvent());
        }

        public int GetNodeLevel(int graphId, int nodeId)
        {
            var graph = GetGraphOrThrow(graphId);
            GetNodeOrThrow(graph, nodeId);
            return _store.GetNodeLevel(graphId, nodeId);
        }

        public bool CanUpgrade(int graphId, int nodeId)
        {
            var graph = GetGraphOrThrow(graphId);
            var node = GetNodeOrThrow(graph, nodeId);
            return CanUpgradeCore(graph, node);
        }

        public bool TryUpgrade(int graphId, int nodeId)
        {
            var graph = GetGraphOrThrow(graphId);
            var node = GetNodeOrThrow(graph, nodeId);
            if (!CanUpgradeCore(graph, node))
                return false;

            var currentLevel = _store.GetNodeLevel(graphId, nodeId);
            var cost = node.UpgradeCosts[currentLevel];
            _store.ApplyUpgrade(graphId, nodeId, cost);
            Publish(new PointAllocationChangedEvent());
            return true;
        }

        public void ResetGraph(int graphId)
        {
            var graph = GetGraphOrThrow(graphId);

            long refund = 0;
            var changed = false;
            for (var index = 0; index < graph.Nodes.Length; index++)
            {
                var node = graph.Nodes[index];
                var currentLevel = _store.GetNodeLevel(graphId, node.Id);
                if (currentLevel > 0)
                    changed = true;

                for (var level = 0; level < currentLevel; level++)
                    refund += node.UpgradeCosts[level];
            }

            if (!changed)
                return;

            _store.ApplyGraphReset(graphId, (int)refund);
            Publish(new PointAllocationChangedEvent());
        }

        protected override void OnShutdown()
        {
            _store.ClearGraphs();
            _store = null;
        }

        private static void ValidateState(
            PointAllocationGraph graph,
            List<PointAllocationNodeState> nodeStates,
            int availablePoints)
        {
            if (nodeStates == null)
                throw new ArgumentNullException(nameof(nodeStates), "节点状态列表 nodeStates 不能为 null。");
            if (availablePoints < 0)
                throw new ArgumentOutOfRangeException(nameof(availablePoints), availablePoints,
                    "可用点数 availablePoints 不能小于 0。");

            var levels = new Dictionary<int, int>();
            for (var index = 0; index < nodeStates.Count; index++)
            {
                var nodeState = nodeStates[index];
                if (nodeState == null)
                    throw new ArgumentException($"节点状态列表 nodeStates 的第 {index} 项为 null。",
                        nameof(nodeStates));

                var node = GetNodeOrThrow(graph, nodeState.NodeId);

                if (nodeState.Level <= 0 || nodeState.Level > node.MaxLevel)
                    throw new ArgumentException(
                        $"加点图 {graph.GraphId} 的节点 {nodeState.NodeId} 等级为 {nodeState.Level}；" +
                        $"稀疏节点状态的等级必须在 1 到 {node.MaxLevel} 之间。",
                        nameof(nodeStates));

                if (!levels.TryAdd(
                        nodeState.NodeId,
                        nodeState.Level))
                    throw new ArgumentException(
                        $"节点 {nodeState.NodeId} 存在重复状态。",
                        nameof(nodeStates));
            }

            foreach (var pair in levels)
            {
                var nodeId = pair.Key;
                var incoming = graph.GetIncomingConnections(nodeId);
                for (var index = 0; index < incoming.Count; index++)
                {
                    var connection = incoming[index];
                    levels.TryGetValue(
                        connection.FromNodeId,
                        out var sourceLevel);
                    if (sourceLevel >= connection.RequiredLevel)
                        continue;

                    throw new ArgumentException(
                        $"节点 {nodeId} 要求节点 {connection.FromNodeId} " +
                        $"达到等级 {connection.RequiredLevel}，但其当前等级为 {sourceLevel}。",
                        nameof(nodeStates));
                }
            }
        }

        private PointAllocationGraph GetGraphOrThrow(int graphId)
        {
            if (_store.TryGetGraph(graphId, out var graph))
                return graph;
            throw new KeyNotFoundException(
                $"加点图 {graphId} 尚未加载。");
        }

        private static PointAllocationNode GetNodeOrThrow(
            PointAllocationGraph graph,
            int nodeId)
        {
            if (graph.TryGetNode(nodeId, out var node))
                return node;
            throw new KeyNotFoundException(
                $"加点图 {graph.GraphId} 中不存在节点 {nodeId}。");
        }

        private bool CanUpgradeCore(PointAllocationGraph graph, PointAllocationNode node)
        {
            var currentLevel = _store.GetNodeLevel(graph.GraphId, node.Id);
            if (currentLevel >= node.MaxLevel)
                return false;

            var incoming = graph.GetIncomingConnections(node.Id);
            for (var index = 0; index < incoming.Count; index++)
            {
                var connection = incoming[index];
                if (_store.GetNodeLevel(graph.GraphId, connection.FromNodeId) < connection.RequiredLevel)
                    return false;
            }

            return _store.AvailablePoints >= node.UpgradeCosts[currentLevel];
        }
    }
}
