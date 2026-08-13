using System;

namespace July.PointAllocation
{
    /// <summary>一项节点升级规则。</summary>
    [Serializable]
    public sealed class PointAllocationNode
    {
        public int Id;
        public int MaxLevel;
        public int[] UpgradeCosts = Array.Empty<int>();

        public PointAllocationNode() { }

        public PointAllocationNode(
            int id,
            int maxLevel,
            int[] upgradeCosts)
        {
            Id = id;
            MaxLevel = maxLevel;
            UpgradeCosts = upgradeCosts;
        }

    }
}
