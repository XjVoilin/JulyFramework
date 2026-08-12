namespace July.PointAllocation
{
    public enum PointAllocationDefinitionErrorCode
    {
        None = 0,
        InvalidDefinitionId,
        MissingNodes,
        EmptyNodes,
        MissingConnections,
        NullNode,
        InvalidNodeId,
        DuplicateNodeId,
        InvalidMaxRank,
        InvalidRankCosts,
        InvalidRankCost,
        NullConnection,
        UnknownConnectionNode,
        SelfConnection,
        DuplicateConnection,
        InvalidRequiredRank,
        DirectedCycle
    }

    /// <summary>可供 Editor 和运行时共同展示的定义校验错误。</summary>
    public readonly struct PointAllocationDefinitionError
    {
        public PointAllocationDefinitionErrorCode Code { get; }
        public int NodeId { get; }
        public int RelatedNodeId { get; }
        public string Message { get; }

        public PointAllocationDefinitionError(
            PointAllocationDefinitionErrorCode code,
            string message,
            int nodeId = 0,
            int relatedNodeId = 0)
        {
            Code = code;
            Message = message;
            NodeId = nodeId;
            RelatedNodeId = relatedNodeId;
        }

        public override string ToString() => Message ?? Code.ToString();
    }
}
