namespace July.Platform
{
    /// <summary>微信原生直玩的来源与有效入场事实，仅在微信平台提供。</summary>
    public interface IWeChatDirectPlayService : IPlatformService
    {
        bool IsDirectPlayLaunch { get; }
        bool HasEntered { get; }
    }

    /// <summary>微信原生直玩确认有效入场；项目决定后续登录行为。</summary>
    public readonly struct WeChatDirectPlayEnteredEvent { }
}
