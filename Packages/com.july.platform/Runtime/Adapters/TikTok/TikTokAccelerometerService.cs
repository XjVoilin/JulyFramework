#if JULYGF_DY_MINIGAME
using System;
using System.Collections.Generic;
using TTSDK;
using UnityEngine;

namespace July.Platform
{
    public sealed class TikTokAccelerometerService : AccelerometerServiceBase
    {
        private long _startOperation;
        private long _listeningOperation = -1;
        private long _deviceOperation;
        public override void Init()
        {
            TT.GetAppLifeCycle().OnHide += OnHide;
            TT.GetAppLifeCycle().OnShow += OnShow;
        }
        protected override void OnShutdown()
        {
            TT.GetAppLifeCycle().OnHide -= OnHide;
            TT.GetAppLifeCycle().OnShow -= OnShow;
        }
        private void OnHide() => SetForeground(false);
        private void OnShow(Dictionary<string, object> _) => SetForeground(true);
        protected override void StartDevice(Action<bool, string> completed)
        {
            // TT keeps its callback until Stop; a prior SDK callback cannot become the new sample stream.
            var operation = ++_startOperation;
            TT.StartAccelerometer((x, y, z) =>
            {
                if (_listeningOperation == operation)
                    ReceiveSample(_deviceOperation, new Vector3((float)x, (float)y, (float)z),
                        Time.realtimeSinceStartupAsDouble);
            }, (success, error) => completed(success, success ? null : error));
        }
        protected override void StopDevice(Action<bool, string> completed) => TT.StopAccelerometer(completed);
        protected override void AttachSamples(long operation)
        {
            _deviceOperation = operation;
            _listeningOperation = _startOperation;
        }
        protected override void DetachSamples() => _listeningOperation = -1;
    }
}
#endif
