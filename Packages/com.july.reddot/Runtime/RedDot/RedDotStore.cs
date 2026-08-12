using System;
using System.Collections.Generic;
using System.Linq;
using July.Arch;

namespace July.RedDot
{
    public class RedDotStoreData
    {
        public Dictionary<string, RedDotNode> Nodes = new();
        public bool GlobalEnabled = true;
    }

    public class RedDotStore : StoreBase<RedDotStoreData>
    {
        #region Node installation

        internal bool InstallNode(string key, string parentKey, RedDotType type)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            if (Data.Nodes.ContainsKey(key))
                return false;

            if (!string.IsNullOrEmpty(parentKey) && !Data.Nodes.ContainsKey(parentKey))
                return false;

            var node = new RedDotNode
            {
                Key = key,
                Type = type,
                ParentKey = parentKey,
                ChildKeys = new List<string>(),
                Count = 0,
                CachedTotalCount = 0,
                IsCacheValid = true
            };

            Data.Nodes[key] = node;
            MarkDirty();

            if (!string.IsNullOrEmpty(parentKey) && Data.Nodes.TryGetValue(parentKey, out var parentNode))
            {
                parentNode.ChildKeys.Add(key);
                InvalidateCacheUpward(parentKey);
            }

            return true;
        }

        #endregion

        #region Queries

        public RedDotNode GetNode(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return Data.Nodes.GetValueOrDefault(key);
        }

        public bool Exists(string key)
        {
            return !string.IsNullOrEmpty(key) && Data.Nodes.ContainsKey(key);
        }

        public List<RedDotNode> GetChildren(string key)
        {
            if (!Data.Nodes.TryGetValue(key, out var node))
                return new List<RedDotNode>();

            var children = new List<RedDotNode>();
            foreach (var childKey in node.ChildKeys)
            {
                if (Data.Nodes.TryGetValue(childKey, out var childNode))
                    children.Add(childNode);
            }

            return children;
        }

        public List<RedDotNode> GetLeafNodes()
        {
            return Data.Nodes.Values.Where(n => n.IsLeaf).ToList();
        }

        #endregion

        #region Count operations

        public List<RedDotChangeInfo> SetCount(string key, int count)
        {
            var changes = new List<RedDotChangeInfo>();

            if (!Data.Nodes.TryGetValue(key, out var node))
                return changes;

            if (!node.IsLeaf)
                return changes;

            count = Math.Max(0, count);
            var oldCount = node.Count;
            if (oldCount == count)
                return changes;

            node.Count = count;
            node.IsCacheValid = false;
            MarkDirty();

            changes.Add(new RedDotChangeInfo
            {
                Key = key,
                OldCount = oldCount,
                NewCount = count,
                Type = node.Type
            });

            changes.AddRange(UpdateParentCounts(node.ParentKey));
            return changes;
        }

        public List<RedDotChangeInfo> SetCountBatch(Dictionary<string, int> counts)
        {
            if (counts == null || counts.Count == 0)
                return new List<RedDotChangeInfo>();

            var changeDict = new Dictionary<string, RedDotChangeInfo>();

            foreach (var kvp in counts)
            {
                if (!Data.Nodes.TryGetValue(kvp.Key, out var node) || !node.IsLeaf)
                    continue;

                var newCount = Math.Max(0, kvp.Value);
                var oldCount = node.Count;
                if (oldCount == newCount)
                    continue;

                changeDict[kvp.Key] = new RedDotChangeInfo
                {
                    Key = kvp.Key,
                    OldCount = oldCount,
                    NewCount = newCount,
                    Type = node.Type
                };

                node.Count = newCount;
                node.IsCacheValid = false;
            }

            if (changeDict.Count > 0)
                MarkDirty();

            var affectedParents = new Dictionary<string, int>();
            foreach (var key in counts.Keys)
            {
                if (!Data.Nodes.TryGetValue(key, out var node))
                    continue;

                var parentKey = node.ParentKey;
                while (!string.IsNullOrEmpty(parentKey) && Data.Nodes.TryGetValue(parentKey, out var parentNode))
                {
                    if (!affectedParents.ContainsKey(parentKey))
                        affectedParents[parentKey] = parentNode.CachedTotalCount;
                    parentKey = parentNode.ParentKey;
                }
            }

            foreach (var root in Data.Nodes.Values.Where(n => string.IsNullOrEmpty(n.ParentKey)))
                RecalculateInternal(root);

            foreach (var kvp in affectedParents)
            {
                if (!Data.Nodes.TryGetValue(kvp.Key, out var parentNode))
                    continue;

                var oldCount = kvp.Value;
                var newCount = parentNode.CachedTotalCount;
                if (oldCount != newCount)
                {
                    changeDict[kvp.Key] = new RedDotChangeInfo
                    {
                        Key = kvp.Key,
                        OldCount = oldCount,
                        NewCount = newCount,
                        Type = parentNode.Type
                    };
                }
            }

            return changeDict.Values.ToList();
        }

        public int GetCount(string key)
        {
            if (!Data.GlobalEnabled)
                return 0;

            if (!Data.Nodes.TryGetValue(key, out var node))
                return 0;

            if (!IsNodeEnabled(node))
                return 0;

            return node.IsCacheValid ? node.CachedTotalCount : RecalculateInternal(node);
        }

        #endregion

        #region Enable flags

        public void SetEnabled(string key, bool enabled)
        {
            if (Data.Nodes.TryGetValue(key, out var node))
            {
                node.IsEnabled = enabled;
                MarkDirty();
            }
        }

        public bool GetEnabled(string key)
        {
            if (!Data.Nodes.TryGetValue(key, out var node))
                return false;

            return IsNodeEnabled(node);
        }

        public void SetGlobalEnabled(bool enabled)
        {
            Data.GlobalEnabled = enabled;
            MarkDirty();
        }

        public bool GetGlobalEnabled() => Data.GlobalEnabled;

        #endregion


        #region Private helpers

        private bool IsNodeEnabled(RedDotNode node)
        {
            if (!node.IsEnabled)
                return false;

            var parentKey = node.ParentKey;
            while (!string.IsNullOrEmpty(parentKey) && Data.Nodes.TryGetValue(parentKey, out var parentNode))
            {
                if (!parentNode.IsEnabled)
                    return false;
                parentKey = parentNode.ParentKey;
            }

            return true;
        }

        private void InvalidateCacheUpward(string key)
        {
            while (!string.IsNullOrEmpty(key) && Data.Nodes.TryGetValue(key, out var node))
            {
                node.IsCacheValid = false;
                key = node.ParentKey;
            }
        }

        private List<RedDotChangeInfo> UpdateParentCounts(string parentKey)
        {
            var changes = new List<RedDotChangeInfo>();

            while (!string.IsNullOrEmpty(parentKey) && Data.Nodes.TryGetValue(parentKey, out var parentNode))
            {
                var oldCount = parentNode.CachedTotalCount;
                var newCount = RecalculateInternal(parentNode);

                if (oldCount != newCount)
                {
                    changes.Add(new RedDotChangeInfo
                    {
                        Key = parentKey,
                        OldCount = oldCount,
                        NewCount = newCount,
                        Type = parentNode.Type
                    });
                }

                parentKey = parentNode.ParentKey;
            }

            return changes;
        }

        private int RecalculateInternal(RedDotNode node)
        {
            if (node.IsLeaf)
            {
                node.CachedTotalCount = node.Count;
            }
            else
            {
                var total = node.Count;
                foreach (var childKey in node.ChildKeys)
                {
                    if (Data.Nodes.TryGetValue(childKey, out var childNode))
                    {
                        total += childNode.IsCacheValid
                            ? childNode.CachedTotalCount
                            : RecalculateInternal(childNode);
                    }
                }

                node.CachedTotalCount = total;
            }

            node.IsCacheValid = true;
            return node.CachedTotalCount;
        }

        #endregion
    }
}
