#if JULYGF_WX_MINIGAME
using System;
using UnityEngine;
using WeChatWASM;

namespace July.Platform
{
    public sealed class WeChatAccelerometerService : AccelerometerServiceBase
    {
        private Action<OnAccelerometerChangeListenerResult> _samples;
        public override void Init() { WX.OnHide(OnHide); WX.OnShow(OnShow); }
        protected override void OnShutdown() { WX.OffHide(OnHide); WX.OffShow(OnShow); }
        private void OnHide(GeneralCallbackResult _) => SetForeground(false);
        private void OnShow(OnShowListenerResult _) => SetForeground(true);

        protected override void StartDevice(Action<bool, string> completed)
        {
            WX.StartAccelerometer(new StartAccelerometerOption
            {
                interval = "game",
                success = _ => completed(true, null),
                fail = result => completed(false, result.errMsg),
            });
        }

        protected override void StopDevice(Action<bool, string> completed)
        {
            WX.StopAccelerometer(new StopAccelerometerOption
            {
                success = _ => completed(true, null),
                fail = result => completed(false, result.errMsg),
            });
        }

        protected override void AttachSamples(long operation)
        {
            _samples = sample => ReceiveSample(operation,
                new Vector3((float)sample.x, (float)sample.y, (float)sample.z), Time.realtimeSinceStartupAsDouble);
            WX.OnAccelerometerChange(_samples);
        }

        protected override void DetachSamples()
        {
            if (_samples == null) return; // Not subscribed before Start succeeds or after an earlier detach.
            WX.OffAccelerometerChange(_samples);
            _samples = null;
        }
    }
}
#endif
