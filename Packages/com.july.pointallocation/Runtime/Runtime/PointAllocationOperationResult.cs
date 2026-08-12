namespace July.PointAllocation
{
    public enum PointAllocationFailureReason
    {
        None = 0,
        InvalidDefinition,
        DuplicateDefinition,
        DefinitionNotFound,
        InvalidProgress,
        InvalidAmount,
        NodeNotFound,
        MaxRankReached,
        PrerequisiteNotMet,
        InsufficientPoints,
        RankIsZero,
        DependentNodeInvested,
        PointOverflow
    }

    /// <summary>预演和修改命令使用的统一结果。</summary>
    public readonly struct PointAllocationOperationResult
    {
        public bool Success { get; }
        public PointAllocationFailureReason FailureReason { get; }
        public int NodeId { get; }
        public int RelatedNodeId { get; }
        public int RequiredValue { get; }
        public int ActualValue { get; }

        private PointAllocationOperationResult(
            bool success,
            PointAllocationFailureReason failureReason,
            int nodeId,
            int relatedNodeId,
            int requiredValue,
            int actualValue)
        {
            Success = success;
            FailureReason = failureReason;
            NodeId = nodeId;
            RelatedNodeId = relatedNodeId;
            RequiredValue = requiredValue;
            ActualValue = actualValue;
        }

        internal static PointAllocationOperationResult Succeeded() =>
            new PointAllocationOperationResult(true, PointAllocationFailureReason.None, 0, 0, 0, 0);

        internal static PointAllocationOperationResult Failed(
            PointAllocationFailureReason reason,
            int nodeId = 0,
            int relatedNodeId = 0,
            int requiredValue = 0,
            int actualValue = 0) =>
            new PointAllocationOperationResult(
                false,
                reason,
                nodeId,
                relatedNodeId,
                requiredValue,
                actualValue);
    }
}

