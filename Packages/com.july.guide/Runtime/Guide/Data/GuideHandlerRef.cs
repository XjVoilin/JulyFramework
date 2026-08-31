using System;

namespace July.Guide
{
    /// <summary>
    /// 配置表对可扩展处理器的引用。Type 选择处理器，ParamId 由处理器到项目 Store 中解释。
    /// </summary>
    [Serializable]
    public struct GuideHandlerRef
    {
        public int Type;
        public int ParamId;

        public GuideHandlerRef(int type, int paramId = 0)
        {
            Type = type;
            ParamId = paramId;
        }
    }
}
