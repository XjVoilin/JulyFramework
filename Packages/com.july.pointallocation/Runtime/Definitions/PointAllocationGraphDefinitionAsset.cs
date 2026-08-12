using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace July.PointAllocation
{
    /// <summary>加点图编辑器导出的运行时定义资产，不包含任何画布或项目 UI 数据。</summary>
    public sealed class PointAllocationGraphDefinitionAsset : ScriptableObject
    {
        [FormerlySerializedAs("_treeId")]
        [SerializeField] private int _definitionId = 1;
        [SerializeField] private PointAllocationNodeDefinition[] _nodes = Array.Empty<PointAllocationNodeDefinition>();
        [SerializeField] private PointAllocationConnectionDefinition[] _connections = Array.Empty<PointAllocationConnectionDefinition>();

        public int DefinitionId => _definitionId;
        public IReadOnlyList<PointAllocationNodeDefinition> Nodes => _nodes ?? Array.Empty<PointAllocationNodeDefinition>();
        public IReadOnlyList<PointAllocationConnectionDefinition> Connections =>
            _connections ?? Array.Empty<PointAllocationConnectionDefinition>();

        public bool TryCreateDefinition(
            out PointAllocationGraphDefinition definition,
            out IReadOnlyList<PointAllocationDefinitionError> errors)
        {
            return PointAllocationGraphDefinition.TryCreate(
                _definitionId,
                _nodes ?? Array.Empty<PointAllocationNodeDefinition>(),
                _connections ?? Array.Empty<PointAllocationConnectionDefinition>(),
                out definition,
                out errors);
        }

        internal void ReplaceDefinition(
            int definitionId,
            IReadOnlyList<PointAllocationNodeDefinition> nodes,
            IReadOnlyList<PointAllocationConnectionDefinition> connections)
        {
            _definitionId = definitionId;
            _nodes = new PointAllocationNodeDefinition[nodes.Count];
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                _nodes[index] = new PointAllocationNodeDefinition(node.Id, node.MaxRank, node.RankCosts);
            }

            _connections = new PointAllocationConnectionDefinition[connections.Count];
            for (var index = 0; index < connections.Count; index++)
            {
                var connection = connections[index];
                _connections[index] = new PointAllocationConnectionDefinition(
                    connection.FromNodeId,
                    connection.ToNodeId,
                    connection.RequiredRank);
            }
        }
    }
}
