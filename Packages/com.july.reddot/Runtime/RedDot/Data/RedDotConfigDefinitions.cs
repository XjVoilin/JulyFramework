using System;
using System.Collections.Generic;

namespace July.RedDot
{
    /// <summary>
    /// 红点节点配置
    /// </summary>
    [Serializable]
    public class RedDotNodeConfig
    {
        public string Key { get; set; }
        public string ParentKey { get; set; }
        public RedDotType Type { get; set; } = RedDotType.Normal;

        public (string Key, string ParentKey, RedDotType Type) ToRegistration()
        {
            return (Key, ParentKey, Type);
        }
    }

    /// <summary>
    /// 红点配置表
    /// </summary>
    [Serializable]
    public class RedDotConfigTable
    {
        public List<RedDotNodeConfig> Nodes { get; set; } = new();

        public List<(string Key, string ParentKey, RedDotType Type)> ToRegistrations()
        {
            var result = new List<(string, string, RedDotType)>();
            if (Nodes != null)
            {
                foreach (var config in Nodes)
                {
                    result.Add(config.ToRegistration());
                }
            }
            return result;
        }
    }
}

