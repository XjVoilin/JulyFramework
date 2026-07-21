using System.Collections.Generic;

namespace July.Guide
{
    /// <summary>
    /// 引导步骤数据
    /// </summary>
    public class GuideStepData
    {
        /// <summary>
        /// 步骤ID
        /// </summary>
        public string StepId;

        /// <summary>
        /// 所属流程ID
        /// </summary>
        public string FlowId;

        /// <summary>
        /// 下一步骤ID（null表示流程结束）
        /// </summary>
        public string NextStepId;

        /// <summary>
        /// 表现配置ID（对应配置表中的ConfigId）
        /// 项目层通过此ID从配置表获取表现配置
        /// 如果为空，则使用 Payload（向后兼容）
        /// </summary>
        public string PresentationConfigId;
    }
}
