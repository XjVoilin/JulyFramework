using System;

namespace July.PointAllocation
{
    /// <summary>由整张图拥有的一项有向前置规则。</summary>
    [Serializable]
    public sealed class PointAllocationConnection
    {
        public int FromNodeId;
        public int ToNodeId;
        public int RequiredLevel;

        public PointAllocationConnection() { }

        public PointAllocationConnection(int fromNodeId, int toNodeId, int requiredLevel)
        {
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            RequiredLevel = requiredLevel;
        }
    }
}
