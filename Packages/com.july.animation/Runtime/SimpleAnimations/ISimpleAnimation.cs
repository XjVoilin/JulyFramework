namespace July.Animation
{
    /// <summary>
    /// 简单待机动画的统一接口，适用于 UI 和非 UI 场景。
    /// 实现类通常在 OnEnable 自动 Play、OnDisable 自动 Stop。
    /// </summary>
    public interface ISimpleAnimation
    {
        bool IsPlaying { get; }
        void Play();
        void Stop();
    }
}
