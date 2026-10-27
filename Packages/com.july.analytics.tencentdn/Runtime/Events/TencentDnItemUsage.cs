using System;

namespace July.Analytics.TencentDn
{
    /// <summary>最小关卡循环内的一种道具消耗，类型编号由项目按腾讯道具枚举映射。</summary>
    public readonly struct TencentDnItemUsage
    {
        public readonly int ItemType;
        public readonly int ItemCount;

        public TencentDnItemUsage(int itemType, int itemCount)
        {
            ItemType = itemType;
            ItemCount = itemCount;
        }

        // 腾讯文档要求 items 为字符串数组，元素采用此格式，不能改成对象数组或整体 JSON 字符串。
        internal string ToProtocolString() =>
            FormattableString.Invariant($"{{item_type:{ItemType},item_num:{ItemCount}}}");
    }
}
