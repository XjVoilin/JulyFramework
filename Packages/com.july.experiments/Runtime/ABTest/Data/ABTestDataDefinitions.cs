using System;
using System.Collections.Generic;

namespace July.Experiments
{
    /// <summary>
    /// 实验状态
    /// </summary>
    public enum ExperimentStatus
    {
        /// <summary>
        /// 草稿（未启用）
        /// </summary>
        Draft,

        /// <summary>
        /// 运行中
        /// </summary>
        Running,

        /// <summary>
        /// 已暂停
        /// </summary>
        Paused,

        /// <summary>
        /// 已结束
        /// </summary>
        Ended,

        /// <summary>
        /// 已归档
        /// </summary>
        Archived
    }

    /// <summary>
    /// 分组分配策略
    /// </summary>
    public enum AllocationStrategy
    {
        /// <summary>
        /// 随机分配（按权重）
        /// </summary>
        Random,

        /// <summary>
        /// 用户ID哈希（确定性分配，相同用户始终进入相同分组）
        /// </summary>
        UserIdHash,

        /// <summary>
        /// 设备ID哈希
        /// </summary>
        DeviceIdHash,

        /// <summary>
        /// 白名单（指定用户进入指定分组）
        /// </summary>
        Whitelist,

        /// <summary>
        /// 自定义分配
        /// </summary>
        Custom
    }

    /// <summary>
    /// 实验分组
    /// </summary>
    [Serializable]
    public class ExperimentGroup
    {
        /// <summary>
        /// 分组ID
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// 分组名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 分组描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 流量权重（0-100）
        /// </summary>
        public int Weight { get; set; } = 50;

        /// <summary>
        /// 是否为对照组
        /// </summary>
        public bool IsControl { get; set; }

        /// <summary>
        /// 分组参数（键值对配置）
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 白名单用户ID列表
        /// </summary>
        public List<string> WhitelistUserIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// 实验目标指标
    /// </summary>
    [Serializable]
    public class ExperimentMetric
    {
        /// <summary>
        /// 指标ID
        /// </summary>
        public string MetricId { get; set; }

        /// <summary>
        /// 指标名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 指标类型（conversion/revenue/retention/custom）
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 是否为主要指标
        /// </summary>
        public bool IsPrimary { get; set; }

        /// <summary>
        /// 目标方向（higher_is_better/lower_is_better）
        /// </summary>
        public string Direction { get; set; } = "higher_is_better";
    }

    /// <summary>
    /// 用户进入条件
    /// </summary>
    [Serializable]
    public class EntryCondition
    {
        /// <summary>
        /// 条件类型
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 条件参数
        /// </summary>
        public string Param { get; set; }

        /// <summary>
        /// 操作符（eq/ne/gt/gte/lt/lte/in/not_in/contains）
        /// </summary>
        public string Operator { get; set; }

        /// <summary>
        /// 目标值
        /// </summary>
        public object Value { get; set; }
    }

    /// <summary>
    /// 实验定义
    /// </summary>
    [Serializable]
    public class Experiment
    {
        /// <summary>
        /// 实验ID（唯一标识）
        /// </summary>
        public string ExperimentId { get; set; }

        /// <summary>
        /// 实验名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 实验描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 实验状态
        /// </summary>
        public ExperimentStatus Status { get; set; } = ExperimentStatus.Draft;

        /// <summary>
        /// 分配策略
        /// </summary>
        public AllocationStrategy Strategy { get; set; } = AllocationStrategy.UserIdHash;

        /// <summary>
        /// 实验分组列表
        /// </summary>
        public List<ExperimentGroup> Groups { get; set; } = new List<ExperimentGroup>();

        /// <summary>
        /// 目标指标列表
        /// </summary>
        public List<ExperimentMetric> Metrics { get; set; } = new List<ExperimentMetric>();

        /// <summary>
        /// 进入条件列表（所有条件需满足）
        /// </summary>
        public List<EntryCondition> EntryConditions { get; set; } = new List<EntryCondition>();

        /// <summary>
        /// 互斥实验ID列表（用户只能参与其中一个）
        /// </summary>
        public List<string> MutualExclusionIds { get; set; } = new List<string>();

        /// <summary>
        /// 流量百分比（0-100，实验整体流量占比）
        /// </summary>
        public int TrafficPercentage { get; set; } = 100;

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 优先级（数值越大优先级越高）
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 实验层级（用于分层实验）
        /// </summary>
        public string Layer { get; set; }

        /// <summary>
        /// 自定义标签
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// 获取对照组
        /// </summary>
        public ExperimentGroup GetControlGroup()
        {
            return Groups?.Find(g => g.IsControl);
        }

        /// <summary>
        /// 获取总权重
        /// </summary>
        public int GetTotalWeight()
        {
            if (Groups == null || Groups.Count == 0) return 0;
            int total = 0;
            foreach (var group in Groups)
            {
                total += group.Weight;
            }
            return total;
        }

        /// <summary>
        /// 检查实验是否在有效期内
        /// </summary>
        public bool IsInValidPeriod(DateTime? checkTime = null)
        {
            var now = checkTime ?? DateTime.UtcNow;

            if (StartTime.HasValue && now < StartTime.Value)
                return false;

            if (EndTime.HasValue && now > EndTime.Value)
                return false;

            return true;
        }

        /// <summary>
        /// 检查实验是否可用
        /// </summary>
        public bool IsAvailable(DateTime? checkTime = null)
        {
            return Status == ExperimentStatus.Running && IsInValidPeriod(checkTime);
        }
    }

    /// <summary>
    /// 用户实验分配记录
    /// </summary>
    [Serializable]
    public class UserExperimentAssignment
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 实验ID
        /// </summary>
        public string ExperimentId { get; set; }

        /// <summary>
        /// 分配的分组ID
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// 分配时间
        /// </summary>
        public DateTime AssignedTime { get; set; }

        /// <summary>
        /// 首次曝光时间
        /// </summary>
        public DateTime? FirstExposureTime { get; set; }

        /// <summary>
        /// 分配时的实验版本（用于追踪配置变更）
        /// </summary>
        public int ExperimentVersion { get; set; }
    }

    /// <summary>
    /// 曝光记录
    /// </summary>
    [Serializable]
    public class ExposureRecord
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 实验ID
        /// </summary>
        public string ExperimentId { get; set; }

        /// <summary>
        /// 分组ID
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// 曝光时间
        /// </summary>
        public DateTime ExposureTime { get; set; }

        /// <summary>
        /// 曝光场景
        /// </summary>
        public string Scene { get; set; }

        /// <summary>
        /// 额外数据
        /// </summary>
        public Dictionary<string, object> ExtraData { get; set; }
    }

    /// <summary>
    /// AB测试存档数据
    /// </summary>
    [Serializable]
    public class ABTestSaveData
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 用户分配记录
        /// </summary>
        public Dictionary<string, UserExperimentAssignment> Assignments { get; set; } = new Dictionary<string, UserExperimentAssignment>();
    }
}

