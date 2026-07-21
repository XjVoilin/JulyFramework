
namespace July.Platform
{
    /// <summary>
    /// 抖音推荐流 SDK 胶水层。
    ///
    /// 仅暴露 SDK 能力原语与状态，不承载业务识别/解析。
    /// 业务识别（是否推荐流直出）与参数解析由 Route 层的 Handler 完成。
    /// 仅 TikTok 平台有实现，其它平台 <c>GetSystem&lt;IPlatformSystem&gt;().GetService&lt;ITikTokFeedService&gt;()</c> 返回 null。
    /// </summary>
    public interface ITikTokFeedService : IPlatformService
    {
        /// <summary>
        /// 通用场景上报（封装 <c>TT.ReportScene</c>）。业务语义由调用方决定，例如：
        /// <list type="bullet">
        /// <item>7001：游戏主场景可交互</item>
        /// </list>
        /// </summary>
        void ReportScene(int sceneId);
    }

    /// <summary>
    /// 推荐流进出状态变化事件。由 <see cref="ITikTokFeedService"/> 在 SDK 回调时发布。
    /// </summary>
    public readonly struct FeedStatusChangedEvent
    {
        public readonly bool IsEnter;
        public FeedStatusChangedEvent(bool isEnter) => IsEnter = isEnter;
    }
}

