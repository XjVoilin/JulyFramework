#if JULYGF_WX_MINIGAME
using July.Arch;
using UnityEngine;
using WeChatWASM;

namespace July.Platform
{
    public sealed class WeChatAccelerometerService : IAccelerometerService, ICanEvent
    {
        public void Start()
        {
            WX.StartAccelerometer(new StartAccelerometerOption
            {
                success = _ =>
                {
                    WX.OnAccelerometerChange(OnAccelerometerChanged);
                    this.Publish(new AccelerometerStartResultEvent(true));
                },
                fail = result => this.Publish(new AccelerometerStartResultEvent(false, result.errMsg)),
            });
        }

        public void Stop()
        {
            WX.OffAccelerometerChange(OnAccelerometerChanged);
            WX.StopAccelerometer(new StopAccelerometerOption
            {
                success = _ => this.Publish(new AccelerometerStopResultEvent(true)),
                fail = result => this.Publish(new AccelerometerStopResultEvent(false, result.errMsg)),
            });
        }

        public void Shutdown() => Stop();

        private void OnAccelerometerChanged(OnAccelerometerChangeListenerResult sample) =>
            this.Publish(new AccelerometerSampleEvent(
                new Vector3((float)sample.x, (float)sample.y, (float)sample.z),
                Time.realtimeSinceStartupAsDouble));
    }
}
#endif
