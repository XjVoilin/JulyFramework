using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace July.RedDot
{
    /// <summary>
    /// 红点树配置（ScriptableObject）
    /// 用于在 Editor 中可视化编辑红点树结构，并导出为代码
    /// </summary>
    [CreateAssetMenu(fileName = "RedDotTreeConfig", menuName = "JulyGF/RedDot/Tree Config", order = 1)]
    public class RedDotTreeConfig : ScriptableObject
    {
        [Header("代码生成设置")]
        [Tooltip("生成的代码命名空间")]
        public string codeNamespace = "Game.RedDot";
        
        [Tooltip("生成的类名")]
        public string className = "RedDotKeys";
        
        [Tooltip("代码输出路径（相对于 Assets）")]
        public string outputPath = "Game/Scripts/RedDot/Generated";

        [Header("红点节点配置")]
        [SerializeField]
        public List<RedDotNodeDefinition> nodes = new List<RedDotNodeDefinition>();

        /// <summary>
        /// 获取所有根节点
        /// </summary>
        public List<RedDotNodeDefinition> GetRootNodes()
        {
            return nodes.Where(n => string.IsNullOrEmpty(n.parentKey)).ToList();
        }

        /// <summary>
        /// 获取指定节点的子节点
        /// </summary>
        public List<RedDotNodeDefinition> GetChildren(string parentKey)
        {
            return nodes.Where(n => n.parentKey == parentKey).ToList();
        }

        /// <summary>
        /// 根据 Key 获取节点（返回第一个匹配的节点，如果存在多个相同key的节点，请使用 GetNode(string key, string parentKey)）
        /// </summary>
        public RedDotNodeDefinition GetNode(string key)
        {
            return nodes.FirstOrDefault(n => n.key == key);
        }

        /// <summary>
        /// 根据 Key 和 ParentKey 精确获取节点
        /// </summary>
        public RedDotNodeDefinition GetNode(string key, string parentKey)
        {
            return nodes.FirstOrDefault(n => n.key == key && n.parentKey == parentKey);
        }

        /// <summary>
        /// 获取所有匹配指定 Key 的节点
        /// </summary>
        public List<RedDotNodeDefinition> GetNodes(string key)
        {
            return nodes.Where(n => n.key == key).ToList();
        }

        /// <summary>
        /// 检查 Key 是否存在（可能有多个相同key的节点）
        /// </summary>
        public bool HasKey(string key)
        {
            return nodes.Any(n => n.key == key);
        }

        /// <summary>
        /// 获取节点的完整路径（从根节点到当前节点的key列表）
        /// </summary>
        public IReadOnlyList<string> GetNodePathKeys(RedDotNodeDefinition node)
            => RedDotKeyPath.GetSegments(this, node);

        /// <summary>
        /// 获取节点的路径字符串（用于唯一性检查）
        /// </summary>
        private string GetNodePathString(RedDotNodeDefinition node)
        {
            return RedDotKeyPath.GetRuntimeKey(this, node);
        }

        /// <summary>
        /// 获取节点的路径字符串（通过key和parentKey）
        /// </summary>
        private string GetNodePathString(string key, string parentKey)
        {
            return GetNodePathString(new RedDotNodeDefinition { key = key, parentKey = parentKey });
        }

        /// <summary>
        /// 检查路径是否存在
        /// </summary>
        private bool HasPath(string pathString)
        {
            foreach (var node in nodes)
            {
                var nodePath = GetNodePathString(node);
                if (nodePath == pathString)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 通过路径字符串获取节点
        /// </summary>
        public RedDotNodeDefinition GetNodeByPath(string pathString)
        {
            foreach (var node in nodes)
            {
                var nodePath = GetNodePathString(node);
                if (nodePath == pathString)
                    return node;
            }
            return null;
        }

        public string GetRuntimeKey(RedDotNodeDefinition node)
            => RedDotKeyPath.GetRuntimeKey(this, node);

        public string GetDisplayPath(RedDotNodeDefinition node)
            => RedDotKeyPath.GetDisplayPath(this, node);

        public string GetCodeIdentifier(RedDotNodeDefinition node)
            => RedDotKeyPath.GetCodeIdentifier(this, node);

        /// <summary>
        /// 添加节点（检查路径唯一性，允许相同key但禁止路径重复）
        /// </summary>
        public bool AddNode(RedDotNodeDefinition node)
        {
            if (string.IsNullOrEmpty(node.key))
                return false;

            // 检查路径是否重复
            var pathString = GetNodePathString(node);
            if (HasPath(pathString))
                return false;

            nodes.Add(node);
            return true;
        }

        /// <summary>
        /// 删除节点（包含子节点）- 删除所有匹配key的节点
        /// </summary>
        public void RemoveNode(string key)
        {
            // 先删除所有子节点
            var children = GetChildren(key);
            foreach (var child in children)
            {
                RemoveNode(child.key);
            }

            nodes.RemoveAll(n => n.key == key);
        }

        /// <summary>
        /// 精确删除节点（通过key和parentKey）
        /// </summary>
        public bool RemoveNode(string key, string parentKey)
        {
            // 先删除所有子节点
            var children = GetChildren(key);
            foreach (var child in children)
            {
                RemoveNode(child.key, key);
            }

            var removed = nodes.RemoveAll(n => n.key == key && n.parentKey == parentKey);
            return removed > 0;
        }

        /// <summary>
        /// 重命名节点（通过key和parentKey精确定位）
        /// </summary>
        /// <param name="oldKey">旧 Key</param>
        /// <param name="oldParentKey">旧父节点 Key</param>
        /// <param name="newKey">新 Key</param>
        /// <returns>是否成功</returns>
        public bool RenameNode(string oldKey, string oldParentKey, string newKey)
        {
            if (string.IsNullOrEmpty(newKey))
                return false;

            if (oldKey == newKey)
                return true; // 没有变化

            var node = GetNode(oldKey, oldParentKey);
            if (node == null)
                return false; // 节点不存在

            // 计算新路径
            var newPath = GetNodePathString(newKey, node.parentKey);
            if (HasPath(newPath))
                return false; // 新路径已存在

            // 更新节点本身的 key
            node.key = newKey;

            // 更新所有子节点的 parentKey 引用（只更新当前节点的直接子节点）
            var children = GetChildren(oldKey);
            foreach (var child in children)
            {
                if (child.parentKey == oldKey)
                {
                    child.parentKey = newKey;
                }
            }

            return true;
        }

        /// <summary>
        /// 重命名节点（兼容旧方法，但只能重命名第一个匹配的节点）
        /// </summary>
        /// <param name="oldKey">旧 Key</param>
        /// <param name="newKey">新 Key</param>
        /// <returns>是否成功</returns>
        [System.Obsolete("请使用 RenameNode(string oldKey, string oldParentKey, string newKey) 方法以精确定位节点")]
        public bool RenameNode(string oldKey, string newKey)
        {
            var node = GetNode(oldKey);
            if (node == null)
                return false;

            return RenameNode(oldKey, node.parentKey, newKey);
        }

        /// <summary>
        /// 验证配置
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            // 检查空 Key
            var emptyKeys = nodes.Where(n => string.IsNullOrEmpty(n.key)).ToList();
            if (emptyKeys.Count > 0)
            {
                errors.Add($"存在 {emptyKeys.Count} 个空 Key 的节点");
            }

            foreach (var node in nodes.Where(n => !string.IsNullOrEmpty(n?.key)))
            {
                if (node.key.Contains(RedDotKeyPath.RuntimeSeparator))
                    errors.Add($"节点 Key '{node.key}' 不能包含运行时路径分隔符 '{RedDotKeyPath.RuntimeSeparator}'");
            }

            // 检查父节点是否存在（需要考虑相同key的情况）
            foreach (var node in nodes)
            {
                if (!string.IsNullOrEmpty(node.parentKey))
                {
                    // 检查是否存在匹配的父节点（key匹配即可，因为parentKey已经指定了父节点）
                    var parentMatches = GetNodes(node.parentKey);
                    if (parentMatches.Count == 0)
                    {
                        errors.Add($"节点 '{node.key}' (父节点: '{node.parentKey}') 的父节点不存在");
                    }
                    else if (parentMatches.Count > 1)
                    {
                        errors.Add($"节点 '{node.key}' 的父节点 Key '{node.parentKey}' 不唯一，无法确定完整路径");
                    }
                }
            }

            // 检查循环依赖（需要考虑相同key的情况）
            foreach (var node in nodes)
            {
                var visited = new HashSet<(string key, string parentKey)>();
                var current = (node.key, node.parentKey);
                while (!string.IsNullOrEmpty(current.key))
                {
                    if (visited.Contains(current))
                    {
                        var path = GetNodePathString(node);
                        errors.Add($"检测到循环依赖，路径: {path}");
                        break;
                    }
                    visited.Add(current);
                    
                    if (string.IsNullOrEmpty(current.parentKey))
                        break;

                    var parentNode = GetNode(current.parentKey);
                    if (parentNode == null)
                        break;

                    current = (parentNode.key, parentNode.parentKey);
                }
            }

            // 检查路径重复
            var pathSet = new Dictionary<string, List<RedDotNodeDefinition>>();
            foreach (var node in nodes)
            {
                if (string.IsNullOrEmpty(node.key))
                    continue;

                var pathString = GetNodePathString(node);
                if (!pathSet.ContainsKey(pathString))
                {
                    pathSet[pathString] = new List<RedDotNodeDefinition>();
                }
                pathSet[pathString].Add(node);
            }

            foreach (var kvp in pathSet)
            {
                if (kvp.Value.Count > 1)
                {
                    errors.Add($"检测到链路重复，路径: {kvp.Key} (共 {kvp.Value.Count} 个节点)");
                }
            }

            var identifierSet = new Dictionary<string, List<RedDotNodeDefinition>>();
            foreach (var node in nodes.Where(n => !string.IsNullOrEmpty(n?.key)))
            {
                var identifier = GetCodeIdentifier(node);
                if (!identifierSet.TryGetValue(identifier, out var matches))
                {
                    matches = new List<RedDotNodeDefinition>();
                    identifierSet.Add(identifier, matches);
                }
                matches.Add(node);
            }

            foreach (var kvp in identifierSet.Where(kvp => kvp.Value.Count > 1))
                errors.Add($"多个节点会生成相同的 C# 标识符 '{kvp.Key}'");

            return errors;
        }

        /// <summary>
        /// 按模块分组获取节点
        /// </summary>
        public Dictionary<string, List<RedDotNodeDefinition>> GetNodesByModule()
        {
            return nodes.GroupBy(n => n.module ?? "Default")
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// 转换为运行时配置表
        /// </summary>
        public RedDotConfigTable ToConfigTable()
        {
            var table = new RedDotConfigTable();
            foreach (var node in nodes)
            {
                table.Nodes.Add(new RedDotNodeConfig
                {
                    Key = RedDotKeyPath.GetRuntimeKey(this, node),
                    ParentKey = RedDotKeyPath.GetParentRuntimeKey(this, node),
                    Type = node.type
                });
            }
            return table;
        }
    }

    /// <summary>
    /// 红点节点定义（用于 Editor 配置）
    /// </summary>
    [Serializable]
    public class RedDotNodeDefinition
    {
        [Tooltip("节点唯一标识（将生成为常量名）")]
        public string key;

        [Tooltip("父节点 Key（空表示根节点）")]
        public string parentKey;

        [Tooltip("红点类型")]
        public RedDotType type = RedDotType.Normal;

        [Tooltip("所属模块（用于代码分组）")]
        public string module;

        [Tooltip("描述/注释")]
        public string description;

        [Tooltip("是否折叠（Editor 用）")]
        [HideInInspector]
        public bool foldout = true;
    }
}

