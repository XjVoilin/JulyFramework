using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace July.PointAllocation
{
    /// <summary>不可变的加点节点定义。</summary>
    [Serializable]
    public sealed class PointAllocationNodeDefinition
    {
        [SerializeField] private int _id;
        [SerializeField] private int _maxRank;
        [SerializeField] private int[] _rankCosts = Array.Empty<int>();

        [NonSerialized] private ReadOnlyCollection<int> _rankCostsView;

        public int Id => _id;
        public int MaxRank => _maxRank;
        public IReadOnlyList<int> RankCosts =>
            _rankCostsView ??= Array.AsReadOnly(_rankCosts ?? Array.Empty<int>());

        public PointAllocationNodeDefinition(int id, int maxRank, IReadOnlyList<int> rankCosts)
        {
            _id = id;
            _maxRank = maxRank;

            if (rankCosts == null)
            {
                _rankCosts = null;
                return;
            }

            _rankCosts = new int[rankCosts.Count];
            for (var index = 0; index < rankCosts.Count; index++)
                _rankCosts[index] = rankCosts[index];
        }
    }
}
