using July.Logging;
using System;
using July.Arch;
using UnityEngine;

namespace July.Platform
{
    /// <summary>期望开关与 SDK 状态分开：停止标记立即生效，未完成的启动收到结果后再收尾。</summary>
    public abstract class AccelerometerServiceBase : IAccelerometerService, ICanEvent
    {
        private enum DeviceState { Idle, Starting, Running, Stopping }
        private DeviceState _state;
        private bool _requested;
        private long _operation;
        private bool _foreground = true;
        private bool _shutdown;
        protected virtual bool IsSupported => true;
        protected const string Unsupported = "Accelerometer is not supported by the default platform adapter.";

        public virtual void Init() { }

        public void Start()
        {
            if (_shutdown) throw new ObjectDisposedException(nameof(IAccelerometerService));
            if (!IsSupported)
            {
                Emit(new AccelerometerStartResultEvent(false, Unsupported));
                return;
            }
            _requested = true;
            Reconcile();
        }

        public void Stop()
        {
            _requested = false;
            DetachSamples();
            if (!IsSupported && !_shutdown) Emit(new AccelerometerStopResultEvent(false, Unsupported));
            Reconcile();
        }

        public void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;
            _requested = false;
            OnShutdown();
            DetachSamples();
            Reconcile();
        }

        protected virtual void OnShutdown() { }
        protected abstract void StartDevice(Action<bool, string> completed);
        protected abstract void StopDevice(Action<bool, string> completed);
        protected abstract void AttachSamples(long operation);
        protected abstract void DetachSamples();
        protected virtual void Emit<T>(T message) => this.Publish(message);
        protected virtual void ReportStopFailure(string error) => JLogger.LogError("[July.Platform] Accelerometer stop failed: " + error);

        protected void SetForeground(bool foreground)
        {
            if (_shutdown) return;
            _foreground = foreground;
            if (!foreground) DetachSamples();
            Reconcile();
        }

        protected void ReceiveSample(long operation, Vector3 acceleration, double timestamp)
        {
            if (_shutdown || !_foreground || _state != DeviceState.Running || operation != _operation ||
                !_requested) return;
            Emit(new AccelerometerSampleEvent(acceleration, timestamp));
        }

        private bool WantsRunning => !_shutdown && _foreground && _requested;

        private void Reconcile()
        {
            if (_state == DeviceState.Idle && WantsRunning)
            {
                _state = DeviceState.Starting;
                var operation = ++_operation;
                StartDevice((success, error) => CompleteStart(operation, success, error));
            }
            else if (_state == DeviceState.Running && !WantsRunning)
            {
                DetachSamples();
                _state = DeviceState.Stopping;
                var operation = ++_operation;
                StopDevice((success, error) => CompleteStop(operation, success, error));
            }
            // Starting/Stopping wait for their real result; no overlapping calls or timeout success.
        }

        private void CompleteStart(long operation, bool success, string error)
        {
            if (operation != _operation || _state != DeviceState.Starting) return; // SDK duplicate/late callback.
            var notify = !_shutdown && _requested;
            _state = success ? DeviceState.Running : DeviceState.Idle;
            if (!success) _requested = false;
            if (success && WantsRunning) AttachSamples(operation);
            if (notify) Emit(new AccelerometerStartResultEvent(success, error));
            Reconcile();
        }

        private void CompleteStop(long operation, bool success, string error)
        {
            if (operation != _operation || _state != DeviceState.Stopping) return;
            // The SDK operation has finished; a stop failure must not discard the current start request.
            _state = DeviceState.Idle;
            if (!success) ReportStopFailure(error); // A synchronous game exit cannot await the SDK result.
            if (!_shutdown) Emit(new AccelerometerStopResultEvent(success, error));
            Reconcile();
        }
    }
}
