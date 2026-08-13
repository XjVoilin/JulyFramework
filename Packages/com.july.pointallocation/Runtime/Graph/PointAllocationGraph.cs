using System;
using System.Collections.Generic;

namespace July.PointAllocation
{
    /// <summary>Editor 导出并由 Runtime 直接读取的静态加点图，不包含节点等级等运行时状态。</summary>
    [Serializable]
    public sealed class PointAllocationGraph
    {
        public int GraphId;
        public PointAllocationNode[] Nodes = Array.Empty<PointAllocationNode>();
        public PointAllocationConnection[] Connections = Array.Empty<PointAllocationConnection>();

        [NonSerialized] private Dictionary<int, PointAllocationNode> _nodes;
        [NonSerialized] private Dictionary<int, List<PointAllocationConnection>> _incomingConnectionsByNodeId;

        public PointAllocationGraph() { }

        public PointAllocationGraph(int graphId, PointAllocationNode[] nodes, PointAllocationConnection[] connections)
        {
            GraphId = graphId;
            Nodes = nodes;
            Connections = connections;
        }

        internal void BuildIndexes()
        {
            _nodes = new Dictionary<int, PointAllocationNode>(Nodes.Length);
            _incomingConnectionsByNodeId =
                new Dictionary<int, List<PointAllocationConnection>>(Nodes.Length);
            foreach (var node in Nodes)
            {
                _nodes[node.Id] = node;
                _incomingConnectionsByNodeId[node.Id] =
                    new List<PointAllocationConnection>();
            }

            foreach (var connection in Connections)
                _incomingConnectionsByNodeId[connection.ToNodeId].Add(connection);
        }

        internal bool TryGetNode(int nodeId, out PointAllocationNode node) =>
            _nodes.TryGetValue(nodeId, out node);

        internal List<PointAllocationConnection> GetIncomingConnections(int nodeId) =>
            _incomingConnectionsByNodeId[nodeId];
    }
}
