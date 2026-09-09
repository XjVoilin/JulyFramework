using UnityEngine;

namespace July.Release
{
    /// <summary>启动配置提供的发布契约；可由项目现有 ScriptableObject 实现。</summary>
    public interface IReleaseBootConfig
    {
        Object Asset { get; }
        ReleaseEnvironment env { get; set; }
        string cdnUrl { get; }
        string EnvName { get; }
        ReleaseResourceSettings Resources { get; }
        string GetConfigServerUrl();
        string GetConfigServerUrl(ReleaseEnvironment environment);
    }
}
