using System.Collections.Generic;
using July.Analytics;

namespace July.Analytics.TencentDn
{
    /// <summary>主玩法关卡场景开始渲染第一帧时上报，同时开启一个最小关卡循环。</summary>
    public readonly struct TencentDnLevelEnterEvent : IBIEvent
    {
        public string EventName => "LEVEL_ENTER";
        public readonly TencentDnLevelInfo Level;
        public readonly string LevelName;

        public TencentDnLevelEnterEvent(TencentDnLevelInfo level, string levelName = null)
        {
            Level = level;
            LevelName = levelName;
        }

        public Dictionary<string, object> ToParams()
        {
            var parameters = Level.CreateParameters();
            if (LevelName != null) parameters.Add("enter_level_name", LevelName);
            return parameters;
        }
    }

    /// <summary>主动中止当前关卡进程时上报，并结束当前最小关卡循环。</summary>
    public readonly struct TencentDnLevelExitEvent : IBIEvent
    {
        public string EventName => "LEVEL_EXIT";
        public readonly TencentDnLevelResult Result;

        public TencentDnLevelExitEvent(TencentDnLevelResult result) => Result = result;
        public Dictionary<string, object> ToParams() => Result.CreateParameters();
    }

    /// <summary>关卡失败时上报，并结束当前最小关卡循环；复活后的新循环由项目重新上报进入。</summary>
    public readonly struct TencentDnLevelLoseEvent : IBIEvent
    {
        public string EventName => "LEVEL_LOSE";
        public readonly TencentDnLevelResult Result;

        public TencentDnLevelLoseEvent(TencentDnLevelResult result) => Result = result;
        public Dictionary<string, object> ToParams() => Result.CreateParameters();
    }

    /// <summary>关卡胜利时上报，并结束当前最小关卡循环。</summary>
    public readonly struct TencentDnLevelPassEvent : IBIEvent
    {
        public string EventName => "LEVEL_PASS";
        public readonly TencentDnLevelResult Result;

        public TencentDnLevelPassEvent(TencentDnLevelResult result) => Result = result;
        public Dictionary<string, object> ToParams() => Result.CreateParameters();
    }
}
