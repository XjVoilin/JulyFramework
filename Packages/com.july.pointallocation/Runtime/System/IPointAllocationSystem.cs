using System.Collections.Generic;

namespace July.PointAllocation
{
    /// <summary>提供加点图加载、状态替换和节点升级操作。</summary>
    public interface IPointAllocationSystem
    {
        int AvailablePoints { get; }

        void LoadGraph(string json);
        void ReplaceState(int graphId, List<PointAllocationNodeState> nodeStates, int availablePoints);
        int GetNodeLevel(int graphId, int nodeId);
        bool CanUpgrade(int graphId, int nodeId);
        bool TryUpgrade(int graphId, int nodeId);
        void ResetGraph(int graphId);
    }
}
