using System;
using System.Collections.Generic;

namespace July.Guide
{
    /// <summary>仅保存持久结果，不保存引导定义或执行中的步骤游标。</summary>
    [Serializable]
    public sealed class GuideStoreData
    {
        public List<int> CompletedGuideIds = new();
        public List<int> SkippedGuideIds = new();
    }
}
