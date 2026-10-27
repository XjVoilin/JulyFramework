using System.Collections.Generic;
using July.Analytics;

namespace July.Analytics.TencentDn
{
    /// <summary>项目确认新注册用户且成功绑定 OpenID 后，通过 track 主动上报。</summary>
    public readonly struct TencentDnRegisterEvent : IBIEvent
    {
        public string EventName => "REGISTER";
        public Dictionary<string, object> ToParams() => new(0);
    }

    /// <summary>
    /// 后台判定满足沉默周期后上报；BackFlowDay 是本游戏固定的回流周期，单位为天。
    /// 沉默唤起和回流使用同一事件，项目负责在绑定 OpenID 后触发并避免重复上报。
    /// </summary>
    public readonly struct TencentDnReactiveEvent : IBIEvent
    {
        public string EventName => "RE_ACTIVE";
        public readonly int BackFlowDay;

        public TencentDnReactiveEvent(int backFlowDay) => BackFlowDay = backFlowDay;

        public Dictionary<string, object> ToParams() => new(1)
        {
            { "backFlowDay", BackFlowDay }
        };
    }

    /// <summary>Loading 页面完成、进入游戏第一帧时上报。</summary>
    public readonly struct TencentDnLoadFinishEvent : IBIEvent
    {
        public string EventName => "LOAD_FINISH";
        public Dictionary<string, object> ToParams() => new(0);
    }

    /// <summary>首次触发具有教学作用的新手引导流程时上报。</summary>
    public readonly struct TencentDnTutorialStartEvent : IBIEvent
    {
        public string EventName => "TUTORIAL_START";
        public Dictionary<string, object> ToParams() => new(0);
    }

    /// <summary>
    /// 完成新手教程或教程关卡时上报，项目通过 track 使用文档标题中的 TUTORIAL_FINISH。
    /// </summary>
    public readonly struct TencentDnTutorialFinishEvent : IBIEvent
    {
        public string EventName => "TUTORIAL_FINISH";
        public Dictionary<string, object> ToParams() => new(0);
    }
}
