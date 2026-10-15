using UnityEngine;

namespace July.Platform
{
    public readonly struct AccelerometerStartResultEvent
    {
        public readonly bool IsSuccess;
        public readonly string Error;

        public AccelerometerStartResultEvent(bool isSuccess, string error = null)
        {
            IsSuccess = isSuccess;
            Error = error;
        }
    }

    public readonly struct AccelerometerStopResultEvent
    {
        public readonly bool IsSuccess;
        public readonly string Error;

        public AccelerometerStopResultEvent(bool isSuccess, string error = null)
        {
            IsSuccess = isSuccess;
            Error = error;
        }
    }

    /// <summary>原始加速度样本，不代表一次晃动，也未进行重力滤波或游戏世界坐标转换。</summary>
    public readonly struct AccelerometerSampleEvent
    {
        /// <summary>
        /// SDK 返回的设备 X/Y/Z 轴原值。当前 SDK 声明未明确单位和横竖屏轴向，
        /// 不宣称为 Unity 世界坐标或 m/s²；跨平台力度参数须经真机标定。
        /// </summary>
        public readonly Vector3 Acceleration;

        /// <summary>接收样本时的 realtimeSinceStartupAsDouble，单位秒，不是硬件采样时间。</summary>
        public readonly double Timestamp;

        public AccelerometerSampleEvent(Vector3 acceleration, double timestamp)
        {
            Acceleration = acceleration;
            Timestamp = timestamp;
        }
    }

    /// <summary>主线程调用。Start/Stop 表达期望开关，平台负责完成 SDK 启停与前后台收尾。</summary>
    public interface IAccelerometerService : IPlatformService
    {
        /// <summary>请求启动，通过 AccelerometerStartResultEvent 通知结果。</summary>
        void Start();

        /// <summary>解除样本监听并请求停止，通过 AccelerometerStopResultEvent 通知结果。</summary>
        void Stop();
    }
}
