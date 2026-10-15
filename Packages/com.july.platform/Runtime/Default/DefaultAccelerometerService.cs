using System;

namespace July.Platform
{
    /// <summary>编辑器及未接入平台没有真实加速度采样，不生成模拟数据。</summary>
    public sealed class DefaultAccelerometerService : AccelerometerServiceBase
    {
        protected override bool IsSupported => false;
        protected override void StartDevice(Action<bool, string> completed) => completed(false, Unsupported);
        protected override void StopDevice(Action<bool, string> completed) => completed(false, Unsupported);
        protected override void AttachSamples(long operation) { }
        protected override void DetachSamples() { }
    }
}
