using System.Collections.Generic;
using July.Persistence;

namespace July.Activity
{
    /// <summary>
    /// 活动状态枚举
    /// </summary>
    public enum ActivityState
    {
        /// <summary>
        /// 未开始
        /// </summary>
        NotStarted = 0,

        /// <summary>
        /// 预告期（可预览但不能参与）
        /// </summary>
        PreAnnounce = 1,

        /// <summary>
        /// 进行中
        /// </summary>
        InProgress = 2,

        /// <summary>
        /// 已结束
        /// </summary>
        Ended = 3
    }

    /// <summary>
    /// 活动定义
    /// </summary>
    public class ActivityDefinition
    {
        /// <summary>
        /// 活动唯一标识
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 活动类型标识
        /// </summary>
        public int Type { get; set; }

        /// <summary>
        /// 预告开始时间（Unix 时间戳，秒）
        /// 设置为 0 或与 StartTime 相同表示无预告期
        /// </summary>
        public long PreAnnounceTime { get; set; }

        /// <summary>
        /// 活动开始时间（Unix 时间戳，秒）
        /// </summary>
        public long StartTime { get; set; }

        /// <summary>
        /// 活动结束时间（Unix 时间戳，秒）
        /// </summary>
        public long EndTime { get; set; }

        /// <summary>
        /// 活动优先级（数值越小优先级越高）
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 扩展数据（项目层自行解释，可存放奖励结构、参与条件等业务数据）
        /// </summary>
        public Dictionary<string, object> ExtData { get; set; } = new();
    }

    /// <summary>
    /// 活动运行时数据
    /// </summary>
    public class ActivityRuntimeData : ISaveData
    {
        /// <summary>
        /// 数据重要性
        /// </summary>
        public SaveImportance Importance => SaveImportance.Important;

        /// <summary>
        /// 各活动的运行时记录
        /// </summary>
        public Dictionary<string, ActivityRecord> RecordMap { get; set; } = new();

        /// <summary>
        /// 已开启过的活动 ID 集合
        /// </summary>
        public HashSet<string> OpenedActivityIds { get; set; } = new();
    }

    /// <summary>
    /// 活动运行时记录
    /// 框架只提供通用的 DataPayload 存储，具体业务数据（奖励领取、积分、排名等）
    /// 由项目层自行序列化到 DataPayload 中
    /// </summary>
    public class ActivityRecord
    {
        /// <summary>
        /// 活动 ID
        /// </summary>
        public string ActivityId { get; set; }

        /// <summary>
        /// 进度数据载荷（JSON 字符串，项目层自行序列化/反序列化）
        /// </summary>
        public string DataPayload { get; set; }

        /// <summary>
        /// 最后更新时间戳
        /// </summary>
        public long LastUpdateTime { get; set; }
    }

    /// <summary>
    /// 活动信息
    /// </summary>
    public class ActivityInfo
    {
        /// <summary>
        /// 活动定义
        /// </summary>
        public ActivityDefinition Definition { get; set; }

        /// <summary>
        /// 当前状态
        /// </summary>
        public ActivityState State { get; set; }

        /// <summary>
        /// 活动运行时记录
        /// </summary>
        public ActivityRecord Record { get; set; }

        /// <summary>
        /// 是否为新开启的活动
        /// </summary>
        public bool IsNewlyOpened { get; set; }
    }
}
