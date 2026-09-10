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
        string GetConfigServerUrl();
        string GetConfigServerUrl(ReleaseEnvironment environment);
    }

    /// <summary>可选：项目已在运行时使用共享资源配置时，发布流程读取同一份数据。</summary>
    public interface IReleaseResourceConfig
    {
        ReleaseResourceSettings Resources { get; }
    }
}
