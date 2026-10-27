using System.Collections.Generic;
using July.Analytics;

namespace July.Analytics.TencentDn
{
    /// <summary>激励视频点位在界面中渲染完成时上报；编号由项目映射腾讯广告行为枚举。</summary>
    public readonly struct TencentDnAdPlacementShowEvent : IBIEvent
    {
        public string EventName => "AD_PLACEMENT_SHOW";
        public readonly int AdPlacementName;

        public TencentDnAdPlacementShowEvent(int adPlacementName) => AdPlacementName = adPlacementName;

        public Dictionary<string, object> ToParams() => new(1)
        {
            { "ad_placement_name", AdPlacementName }
        };
    }

    /// <summary>玩家点击界面中的激励视频点位按钮时上报；编号由项目映射腾讯广告行为枚举。</summary>
    public readonly struct TencentDnAdClickEvent : IBIEvent
    {
        public string EventName => "AD_CLICK";
        public readonly int AdPlacementName;

        public TencentDnAdClickEvent(int adPlacementName) => AdPlacementName = adPlacementName;

        public Dictionary<string, object> ToParams() => new(1)
        {
            { "ad_placement_name", AdPlacementName }
        };
    }

    /// <summary>激励视频完整播放完毕并显示获得奖励时上报，中途关闭不属于此事件。</summary>
    public readonly struct TencentDnAdVideoFinishEvent : IBIEvent
    {
        public string EventName => "AD_VIDEO_FINISH";
        public readonly int AdPlacementName;

        public TencentDnAdVideoFinishEvent(int adPlacementName) => AdPlacementName = adPlacementName;

        public Dictionary<string, object> ToParams() => new(1)
        {
            { "ad_placement_name", AdPlacementName }
        };
    }
}
