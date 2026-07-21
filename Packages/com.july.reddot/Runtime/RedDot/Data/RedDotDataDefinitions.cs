using System;
using System.Collections.Generic;

namespace July.RedDot
{
    /// <summary>
    /// 红点类型
    /// </summary>
    public enum RedDotType
    {
        /// <summary>
        /// 普通红点（显示/隐藏）
        /// </summary>
        Normal,

        /// <summary>
        /// 数字红点（显示数量）
        /// </summary>
        Number,

        /// <summary>
        /// 新标签（NEW）
        /// </summary>
        New
    }

    /// <summary>
    /// 红点节点数据（业务数据）
    /// </summary>
    [Serializable]
    public class RedDotNode
    {
        /// <summary>
        /// 节点Key（唯一标识）
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 红点类型
        /// </summary>
        public RedDotType Type { get; set; } = RedDotType.Normal;

        /// <summary>
        /// 父节点Key（null表示根节点）
        /// </summary>
        public string ParentKey { get; set; }

        /// <summary>
        /// 子节点Key列表
        /// </summary>
        public List<string> ChildKeys { get; set; } = new List<string>();

        /// <summary>
        /// 当前红点数量（叶子节点的值）
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// 缓存的红点数量（包含子节点汇总）
        /// </summary>
        public int CachedTotalCount { get; set; }

        /// <summary>
        /// 是否启用（禁用后 GetCount 返回 0）
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 是否为叶子节点
        /// </summary>
        public bool IsLeaf => ChildKeys == null || ChildKeys.Count == 0;

        /// <summary>
        /// 是否显示红点
        /// </summary>
        public bool IsVisible => IsEnabled && CachedTotalCount > 0;

        /// <summary>
        /// 缓存是否有效
        /// </summary>
        public bool IsCacheValid { get; set; }

        /// <summary>
        /// 自定义数据
        /// </summary>
        public object UserData { get; set; }
    }

    /// <summary>
    /// 红点变更信息
    /// </summary>
    [Serializable]
    public class RedDotChangeInfo
    {
        /// <summary>
        /// 节点Key
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 旧数量
        /// </summary>
        public int OldCount { get; set; }

        /// <summary>
        /// 新数量
        /// </summary>
        public int NewCount { get; set; }

        /// <summary>
        /// 红点类型
        /// </summary>
        public RedDotType Type { get; set; }
    }

    /// <summary>
    /// 红点值计算器委托
    /// </summary>
    /// <param name="key">节点Key</param>
    /// <returns>红点数量</returns>
    public delegate int RedDotValueCalculator(string key);
}

