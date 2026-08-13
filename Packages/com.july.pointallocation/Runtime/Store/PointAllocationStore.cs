using System;
using System.Collections.Generic;
using July.Arch;

namespace July.PointAllocation
{
    /// <summary>已加载加点图与可变加点状态的运行时所有者。</summary>
    public sealed class PointAllocationStore : StoreBase<PointAllocationStoreData>
    {
        private readonly Dictionary<int, PointAllocationGraph> _graphs = new();

        public int AvailablePoints => Data.AvailablePoints;

        internal void AddGraph(PointAllocationGraph graph)
        {
            if (_graphs.ContainsKey(graph.GraphId))
                throw new InvalidOperationException($"加点图 {graph.GraphId} 已经加载，不能重复加载。");
            graph.BuildIndexes();
            _graphs.Add(graph.GraphId, graph);
        }

        public bool TryGetGraph(int graphId, out PointAllocationGraph graph) =>
            _graphs.TryGetValue(graphId, out graph);

        internal int GetNodeLevel(int graphId, int nodeId)
        {
            var graphState = FindGraphState(graphId);
            if (graphState == null)
                return 0;

            foreach (var nodeState in graphState.NodeStates)
            {
                if (nodeState.NodeId == nodeId)
                    return nodeState.Level;
            }

            return 0;
        }

        internal void ReplaceGraphState(
            int graphId,
            List<PointAllocationNodeState> nodeStates,
            int availablePoints)
        {
            Data.GraphStates.RemoveAll(graphState => graphState.GraphId == graphId);
            Data.GraphStates.Add(new PointAllocationGraphState(graphId, nodeStates));
            Data.AvailablePoints = availablePoints;

            MarkDirty();
        }

        internal void ApplyUpgrade(int graphId, int nodeId, int cost)
        {
            var level = GetNodeLevel(graphId, nodeId) + 1;
            var graphState = GetOrCreateGraphState(graphId);
            var found = false;
            foreach (var nodeState in graphState.NodeStates)
            {
                if (nodeState.NodeId != nodeId) continue;
                nodeState.Level = level;
                found = true;
                break;
            }
            if (!found)
                graphState.NodeStates.Add(new PointAllocationNodeState(nodeId, level));

            Data.AvailablePoints -= cost;
            MarkDirty();
        }

        internal void ApplyGraphReset(int graphId, int refund)
        {
            Data.GraphStates.RemoveAll(graphState => graphState.GraphId == graphId);

            Data.AvailablePoints = checked(Data.AvailablePoints + refund);
            MarkDirty();
        }

        internal void ClearGraphs()
        {
            _graphs.Clear();
        }

        private PointAllocationGraphState FindGraphState(int graphId)
        {
            foreach (var graphState in Data.GraphStates)
            {
                if (graphState.GraphId == graphId)
                    return graphState;
            }

            return null;
        }

        private PointAllocationGraphState GetOrCreateGraphState(int graphId)
        {
            var graphState = FindGraphState(graphId);
            if (graphState != null)
                return graphState;

            graphState = new PointAllocationGraphState(
                graphId,
                new List<PointAllocationNodeState>());
            Data.GraphStates.Add(graphState);
            return graphState;
        }
    }
}
