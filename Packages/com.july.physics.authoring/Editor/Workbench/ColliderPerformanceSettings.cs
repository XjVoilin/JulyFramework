using System;

namespace July.Physics.Authoring
{
    [Serializable]
    public sealed class ColliderPerformanceSettings
    {
        public bool Enabled = true;
        public int EnvelopeReminder = 3, EnvelopeAttention = 6;
        public int StructureReminder = 8, StructureAttention = 12;
        public int ConvexReminder = 4, ConvexAttention = 8;
        public int HullVerticesReminder = 16, HullVerticesAttention = 24;
        public int TotalVerticesReminder = 64, TotalVerticesAttention = 128;

        internal ColliderPerformanceSettings Copy() => (ColliderPerformanceSettings)MemberwiseClone();
        internal void Validate()
        {
            Check("整体包围碰撞体数", EnvelopeReminder, EnvelopeAttention);
            Check("结构贴合碰撞体数", StructureReminder, StructureAttention);
            Check("结构贴合凸包数", ConvexReminder, ConvexAttention);
            Check("单凸包顶点数", HullVerticesReminder, HullVerticesAttention);
            Check("结构贴合网格总顶点数", TotalVerticesReminder, TotalVerticesAttention);
        }
        static void Check(string name, int reminder, int attention)
        {
            if (reminder < 0 || attention < reminder)
                throw new ArgumentException(name + "：阈值不能为负，重点检查阈值不能小于提醒阈值。");
        }
    }
}
