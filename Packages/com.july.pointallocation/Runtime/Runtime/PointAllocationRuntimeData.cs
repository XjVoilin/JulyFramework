using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace July.PointAllocation
{
    public readonly struct PointAllocationNodeState
    {
        public int NodeId { get; }
        public int CurrentRank { get; }
        public int MaxRank { get; }
        public int NextRankCost { get; }
        public bool PrerequisitesMet { get; }
        public PointAllocationOperationResult AddRankEvaluation { get; }
        public PointAllocationOperationResult RefundRankEvaluation { get; }

        internal PointAllocationNodeState(
            int nodeId,
            int currentRank,
            int maxRank,
            int nextRankCost,
            bool prerequisitesMet,
            PointAllocationOperationResult addRankEvaluation,
            PointAllocationOperationResult refundRankEvaluation)
        {
            NodeId = nodeId;
            CurrentRank = currentRank;
            MaxRank = maxRank;
            NextRankCost = nextRankCost;
            PrerequisitesMet = prerequisitesMet;
            AddRankEvaluation = addRankEvaluation;
            RefundRankEvaluation = refundRankEvaluation;
        }
    }

    public readonly struct PointAllocationConnectionState
    {
        public PointAllocationConnectionDefinition Definition { get; }
        public int CurrentFromRank { get; }
        public bool Satisfied { get; }

        internal PointAllocationConnectionState(
            PointAllocationConnectionDefinition definition,
            int currentFromRank)
        {
            Definition = definition;
            CurrentFromRank = currentFromRank;
            Satisfied = currentFromRank >= definition.RequiredRank;
        }
    }

    public enum PointAllocationChangeReason
    {
        PointsGranted = 0,
        RankAdded,
        RankRefunded,
        Reset
    }

    public readonly struct PointAllocationNodeRankChange
    {
        public int NodeId { get; }
        public int PreviousRank { get; }
        public int CurrentRank { get; }

        public PointAllocationNodeRankChange(int nodeId, int previousRank, int currentRank)
        {
            NodeId = nodeId;
            PreviousRank = previousRank;
            CurrentRank = currentRank;
        }
    }

    public readonly struct PointAllocationChangedEvent
    {
        public PointAllocationChangeReason Reason { get; }
        public int PreviousAvailablePoints { get; }
        public int CurrentAvailablePoints { get; }
        public IReadOnlyList<PointAllocationNodeRankChange> NodeRankChanges { get; }

        internal PointAllocationChangedEvent(
            PointAllocationChangeReason reason,
            int previousAvailablePoints,
            int currentAvailablePoints,
            IReadOnlyList<PointAllocationNodeRankChange> nodeRankChanges)
        {
            Reason = reason;
            PreviousAvailablePoints = previousAvailablePoints;
            CurrentAvailablePoints = currentAvailablePoints;

            if (nodeRankChanges == null || nodeRankChanges.Count == 0)
            {
                NodeRankChanges = Array.Empty<PointAllocationNodeRankChange>();
                return;
            }

            var snapshot = new PointAllocationNodeRankChange[nodeRankChanges.Count];
            for (var index = 0; index < nodeRankChanges.Count; index++)
                snapshot[index] = nodeRankChanges[index];
            NodeRankChanges = new ReadOnlyCollection<PointAllocationNodeRankChange>(snapshot);
        }
    }

    /// <summary>权威完整进度替换完成后的刷新标记。</summary>
    public readonly struct PointAllocationReplacedEvent
    {
    }
}

