using System;
using System.Collections.Generic;

namespace July.PointAllocation
{
    [Serializable]
    public sealed class PointAllocationNodeState
    {
        public int NodeId;
        public int Level;

        public PointAllocationNodeState() { }

        public PointAllocationNodeState(int nodeId, int level)
        {
            NodeId = nodeId;
            Level = level;
        }
    }

    [Serializable]
    public sealed class PointAllocationGraphState
    {
        public int GraphId;
        public List<PointAllocationNodeState> NodeStates = new List<PointAllocationNodeState>();

        public PointAllocationGraphState() { }

        public PointAllocationGraphState(
            int graphId,
            List<PointAllocationNodeState> nodeStates)
        {
            GraphId = graphId;
            NodeStates = nodeStates;
        }
    }

    [Serializable]
    public sealed class PointAllocationStoreData
    {
        public int AvailablePoints;
        public List<PointAllocationGraphState> GraphStates = new List<PointAllocationGraphState>();
    }
}
