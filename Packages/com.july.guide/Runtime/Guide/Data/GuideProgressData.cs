using System;
using System.Collections.Generic;

namespace July.Guide
{
    /// <summary>
    /// 引导进度数据
    /// 用于持久化引导进度
    /// </summary>
    [Serializable]
    public class GuideProgressData
    {
        /// <summary>
        /// 已完成的流程ID集合
        /// </summary>
        public List<string> completedFlows = new();

        /// <summary>
        /// 已完成的步骤ID集合
        /// </summary>
        public List<string> completedSteps = new();

        /// <summary>
        /// 当前进行中的流程ID
        /// </summary>
        public string currentFlowId;

        /// <summary>
        /// 当前进行中的步骤ID
        /// </summary>
        public string currentStepId;
    }
}

