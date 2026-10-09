#if JULYGF_DY_MINIGAME
using July.Arch;
using TTSDK;
using UnityEngine;

namespace July.Platform
{
    public sealed class TikTokAccelerometerService : IAccelerometerService, ICanEvent
    {
        public void Start() => TT.StartAccelerometer(OnAccelerometerChanged, (success, error) =>
            this.Publish(new AccelerometerStartResultEvent(success, success ? null : error)));

        public void Stop() => TT.StopAccelerometer((success, error) =>
            this.Publish(new AccelerometerStopResultEvent(success, success ? null : error)));

        public void Shutdown() => Stop();

        private void OnAccelerometerChanged(double x, double y, double z) =>
            this.Publish(new AccelerometerSampleEvent(
                new Vector3((float)x, (float)y, (float)z), Time.realtimeSinceStartupAsDouble));
    }
}
#endif
