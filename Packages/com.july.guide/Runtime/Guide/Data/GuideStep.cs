using System;

namespace July.Guide
{
    /// <summary>框架顺序调度的执行步骤，记录编号、执行类型与参数编号。
    /// 可对应一次确认、一次点击或包含多次操作的教学，不限定为最小动作或固定阶段。
    /// 对应 Procedure 独立完成交互并释放其持有的资源后，框架才推进下一步。</summary>
    public sealed class GuideStep
    {
        public int Id { get; }
        public int Type { get; }
        public int ParamId { get; }

        public GuideStep(int id, int type, int paramId = 0)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (type <= 0) throw new ArgumentOutOfRangeException(nameof(type));
            Id = id;
            Type = type;
            ParamId = paramId;
        }
    }
}
