using July.Arch;

namespace July.Platform
{
    /// <summary>编辑器及未接入平台没有真实加速度采样，不生成模拟数据。</summary>
    public sealed class DefaultAccelerometerService : IAccelerometerService, ICanEvent
    {
        private const string Unsupported = "Accelerometer is not supported by the default platform adapter.";

        public void Start() => this.Publish(new AccelerometerStartResultEvent(false, Unsupported));
        public void Stop() => this.Publish(new AccelerometerStopResultEvent(false, Unsupported));
    }
}
