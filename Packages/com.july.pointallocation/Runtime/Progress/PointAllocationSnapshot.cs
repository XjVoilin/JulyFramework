using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace July.PointAllocation
{
    public readonly struct PointAllocationNodeRank
    {
        public int NodeId { get; }
        public int CurrentRank { get; }

        public PointAllocationNodeRank(int nodeId, int currentRank)
        {
            NodeId = nodeId;
            CurrentRank = currentRank;
        }
    }

    /// <summary>可用点数和稀疏节点等级组成的独立进度快照。</summary>
    public readonly struct PointAllocationSnapshot
    {
        public int AvailablePoints { get; }
        public IReadOnlyList<PointAllocationNodeRank> NodeRanks { get; }

        public PointAllocationSnapshot(
            int availablePoints,
            IReadOnlyList<PointAllocationNodeRank> nodeRanks)
        {
            AvailablePoints = availablePoints;
            if (nodeRanks == null || nodeRanks.Count == 0)
            {
                NodeRanks = Array.Empty<PointAllocationNodeRank>();
                return;
            }

            var snapshot = new PointAllocationNodeRank[nodeRanks.Count];
            for (var index = 0; index < nodeRanks.Count; index++)
                snapshot[index] = nodeRanks[index];

            Array.Sort(snapshot, (left, right) => left.NodeId.CompareTo(right.NodeId));
            NodeRanks = new ReadOnlyCollection<PointAllocationNodeRank>(snapshot);
        }

        public static PointAllocationSnapshot Empty(int availablePoints = 0) =>
            new PointAllocationSnapshot(availablePoints, Array.Empty<PointAllocationNodeRank>());
    }
}

