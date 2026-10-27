using System.Collections.Generic;

namespace July.Analytics.TencentDn
{
    /// <summary>
    /// 从一次 LEVEL_ENTER 到退出、失败或胜利的最小循环统计，不在此累计或重置业务计数。
    /// AdCount 仅统计本循环内完整看完的激励视频，不包含退出界面的广告。
    /// </summary>
    public readonly struct TencentDnLevelResult
    {
        public readonly TencentDnLevelInfo Level;
        public readonly int AdCount;
        public readonly int? DurationSeconds;
        private readonly TencentDnItemUsage[] _items;

        public TencentDnLevelResult(TencentDnLevelInfo level, int adCount,
            int? durationSeconds = null, TencentDnItemUsage[] items = null)
        {
            Level = level;
            AdCount = adCount;
            DurationSeconds = durationSeconds;
            // 事件保留触发时的道具快照，调用方后续修改统计数组不应改变待发送事件。
            _items = items == null ? null : (TencentDnItemUsage[])items.Clone();
        }

        internal Dictionary<string, object> CreateParameters()
        {
            var parameters = Level.CreateParameters();
            parameters.Add("ad_cnt", AdCount);
            if (DurationSeconds.HasValue) parameters.Add("duration", DurationSeconds.Value);
            if (_items != null)
            {
                var items = new string[_items.Length];
                for (var i = 0; i < _items.Length; i++) items[i] = _items[i].ToProtocolString();
                parameters.Add("items", items);
            }
            return parameters;
        }
    }
}
