using System.Collections.Generic;

namespace July.Experiments
{
    /// <summary>
    /// 用户分配到实验组事件
    /// </summary>
    public struct UserAssignedToExperimentEvent
    {
        public string UserId;
        public string ExperimentId;
        public string GroupId;
        public Experiment Experiment;
        public ExperimentGroup Group;
        public bool IsNewAssignment;
    }

    /// <summary>
    /// 实验曝光事件
    /// </summary>
    public struct ExperimentExposureEvent
    {
        public string UserId;
        public string ExperimentId;
        public string GroupId;
        public string Scene;
        public bool IsFirstExposure;
        public Dictionary<string, object> ExtraData;
    }

    /// <summary>
    /// 实验状态变更事件
    /// </summary>
    public struct ExperimentStatusChangedEvent
    {
        public string ExperimentId;
        public ExperimentStatus OldStatus;
        public ExperimentStatus NewStatus;
        public Experiment Experiment;
    }

    /// <summary>
    /// 实验配置更新事件
    /// </summary>
    public struct ExperimentConfigUpdatedEvent
    {
        public string ExperimentId;
        public Experiment OldConfig;
        public Experiment NewConfig;
    }

    /// <summary>
    /// 用户退出实验事件
    /// </summary>
    public struct UserExitedExperimentEvent
    {
        public string UserId;
        public string ExperimentId;
        public string GroupId;
        public string Reason;
    }
}
