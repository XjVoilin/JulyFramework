using System;
using UnityEngine;

namespace July.PointAllocation
{
    /// <summary>由整张加点图拥有的不可变有向连接定义。</summary>
    [Serializable]
    public sealed class PointAllocationConnectionDefinition
    {
        [SerializeField] private int _fromNodeId;
        [SerializeField] private int _toNodeId;
        [SerializeField] private int _requiredRank;

        public int FromNodeId => _fromNodeId;
        public int ToNodeId => _toNodeId;
        public int RequiredRank => _requiredRank;

        public PointAllocationConnectionDefinition(int fromNodeId, int toNodeId, int requiredRank)
        {
            _fromNodeId = fromNodeId;
            _toNodeId = toNodeId;
            _requiredRank = requiredRank;
        }
    }
}
