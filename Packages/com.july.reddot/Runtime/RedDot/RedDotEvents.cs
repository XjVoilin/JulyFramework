namespace July.RedDot
{
    /// <summary>
    /// 红点变更事件
    /// </summary>
    public struct RedDotChangedEvent
    {
        public string Key;
        public int OldCount;
        public int NewCount;
        public RedDotType Type;
        public bool JustAppeared => OldCount == 0 && NewCount > 0;
        public bool JustDisappeared => OldCount > 0 && NewCount == 0;
    }

    /// <summary>
    /// 红点启用状态变更事件
    /// </summary>
    public struct RedDotEnabledChangedEvent
    {
        /// <summary>变更的节点 Key（null 表示全局变更）</summary>
        public string Key;

        /// <summary>是否启用</summary>
        public bool Enabled;

        /// <summary>是否是全局变更</summary>
        public bool IsGlobal => Key == null;
    }
}
