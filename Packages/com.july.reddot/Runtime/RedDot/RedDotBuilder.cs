using System;
using System.Collections.Generic;

namespace July.RedDot
{
    /// <summary>
    /// 构建红点系统初始化所需的完整定义。
    /// 节点用于描述红点树结构，Handler 用于绑定业务状态驱动的叶子节点数值。
    /// </summary>
    public sealed class RedDotBuilder
    {
        private readonly List<RedDotNodeSpec> _nodes = new();
        private readonly List<RedDotHandler> _handlers = new();
        private bool _isBuilt;

        internal RedDotBuilder()
        {
        }

        public void AddNode(
            string key,
            string parentKey = null,
            RedDotType type = RedDotType.Normal)
        {
            EnsureOpen();

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("红点 Key 不能为空。", nameof(key));

            _nodes.Add(new RedDotNodeSpec(key, parentKey, type));
        }

        public void BindHandler(RedDotHandler handler)
        {
            EnsureOpen();
            _handlers.Add(handler ?? throw new ArgumentNullException(nameof(handler)));
        }

        internal RedDotDefinition Build()
        {
            EnsureOpen();
            _isBuilt = true;

            var nodesByKey = BuildNodeMap();
            var orderedNodes = SortAndValidateNodes(nodesByKey);
            ValidateHandlers(nodesByKey);

            return new RedDotDefinition(orderedNodes.ToArray(), _handlers.ToArray());
        }

        private void EnsureOpen()
        {
            if (_isBuilt)
                throw new InvalidOperationException("红点定义已经构建完成，不能继续修改。");
        }

        private Dictionary<string, RedDotNodeSpec> BuildNodeMap()
        {
            var nodesByKey = new Dictionary<string, RedDotNodeSpec>(_nodes.Count, StringComparer.Ordinal);
            foreach (var node in _nodes)
            {
                if (!nodesByKey.TryAdd(node.Key, node))
                {
                    throw new InvalidOperationException(
                        $"红点节点“{node.Key}”被重复注册。");
                }
            }

            return nodesByKey;
        }

        private List<RedDotNodeSpec> SortAndValidateNodes(
            Dictionary<string, RedDotNodeSpec> nodesByKey)
        {
            var orderedNodes = new List<RedDotNodeSpec>(_nodes.Count);
            var visitStates = new Dictionary<string, VisitState>(_nodes.Count, StringComparer.Ordinal);

            foreach (var node in _nodes)
                VisitNode(node, nodesByKey, visitStates, orderedNodes);

            return orderedNodes;
        }

        private static void VisitNode(
            RedDotNodeSpec node,
            Dictionary<string, RedDotNodeSpec> nodesByKey,
            Dictionary<string, VisitState> visitStates,
            List<RedDotNodeSpec> orderedNodes)
        {
            if (visitStates.TryGetValue(node.Key, out var state))
            {
                if (state == VisitState.Visited)
                    return;

                throw new InvalidOperationException(
                    $"红点树在节点“{node.Key}”处存在循环引用。");
            }

            visitStates[node.Key] = VisitState.Visiting;

            if (!string.IsNullOrEmpty(node.ParentKey))
            {
                if (!nodesByKey.TryGetValue(node.ParentKey, out var parent))
                {
                    throw new InvalidOperationException(
                        $"红点节点“{node.Key}”引用了不存在的父节点“{node.ParentKey}”。");
                }

                VisitNode(parent, nodesByKey, visitStates, orderedNodes);
            }

            visitStates[node.Key] = VisitState.Visited;
            orderedNodes.Add(node);
        }

        private void ValidateHandlers(Dictionary<string, RedDotNodeSpec> nodesByKey)
        {
            var childCounts = new Dictionary<string, int>(_nodes.Count, StringComparer.Ordinal);
            foreach (var node in _nodes)
                childCounts[node.Key] = 0;

            foreach (var node in _nodes)
            {
                if (!string.IsNullOrEmpty(node.ParentKey))
                    childCounts[node.ParentKey]++;
            }

            var handlerKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var handler in _handlers)
            {
                var key = handler.BindingKey;
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException(
                        $"红点 Handler“{handler.GetType().FullName}”的 Key 为空。");
                }

                if (!nodesByKey.ContainsKey(key))
                {
                    throw new InvalidOperationException(
                        $"红点 Handler“{handler.GetType().FullName}”绑定了不存在的节点“{key}”。");
                }

                if (childCounts[key] > 0)
                {
                    throw new InvalidOperationException(
                        $"红点 Handler“{handler.GetType().FullName}”不能绑定聚合节点“{key}”。");
                }

                if (!handlerKeys.Add(key))
                {
                    throw new InvalidOperationException(
                        $"红点节点“{key}”绑定了多个 Handler。");
                }
            }
        }

        private enum VisitState
        {
            Visiting,
            Visited
        }
    }

    /// <summary>经过完整校验、等待安装的红点定义。</summary>
    internal sealed class RedDotDefinition
    {
        public RedDotNodeSpec[] Nodes { get; }
        public RedDotHandler[] Handlers { get; }

        public RedDotDefinition(RedDotNodeSpec[] nodes, RedDotHandler[] handlers)
        {
            Nodes = nodes;
            Handlers = handlers;
        }
    }

    /// <summary>红点树中的一个节点定义。</summary>
    internal sealed class RedDotNodeSpec
    {
        public string Key { get; }
        public string ParentKey { get; }
        public RedDotType Type { get; }

        public RedDotNodeSpec(string key, string parentKey, RedDotType type)
        {
            Key = key;
            ParentKey = parentKey;
            Type = type;
        }
    }
}
