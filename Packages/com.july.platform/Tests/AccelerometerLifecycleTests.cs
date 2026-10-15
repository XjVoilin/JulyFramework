using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace July.Platform.Tests
{
    public sealed class AccelerometerLifecycleTests
    {
        private sealed class Device : AccelerometerServiceBase
        {
            internal Action<bool, string> Started, Stopped;
            internal int StartCalls, StopCalls, AttachCalls, Failures;
            internal long ListeningOperation;
            internal readonly List<object> Events = new();
            protected override void StartDevice(Action<bool, string> completed) { StartCalls++; Started = completed; }
            protected override void StopDevice(Action<bool, string> completed) { StopCalls++; Stopped = completed; }
            protected override void AttachSamples(long operation) { AttachCalls++; ListeningOperation = operation; }
            protected override void DetachSamples() { }
            protected override void Emit<T>(T message) => Events.Add(message);
            protected override void ReportStopFailure(string error) { Failures++; }
            internal void Visible(bool value) => SetForeground(value);
            internal void Sample(long operation) => ReceiveSample(operation, new Vector3(1, 2, 3), 1);
        }

        [Test] public void StopDuringStart_MarksIntentThenStopsWithoutListening()
        {
            var device = new Device(); device.Start(); device.Stop(); device.Stop();
            Assert.That(device.StopCalls, Is.EqualTo(0));
            device.Started(true, null);
            Assert.That(device.AttachCalls, Is.EqualTo(0));
            Assert.That(device.StopCalls, Is.EqualTo(1));
            Assert.That(device.Events, Is.Empty);
            device.Stopped(true, null);
            Assert.That(device.Events[0] is AccelerometerStopResultEvent, Is.True);
        }

        [Test] public void StopThenStartDuringPendingStart_UsesLatestIntent()
        {
            var device = new Device(); device.Start(); device.Stop(); device.Start();
            device.Started(true, null);
            Assert.That(device.StartCalls, Is.EqualTo(1));
            Assert.That(device.StopCalls, Is.EqualTo(0));
            Assert.That(device.AttachCalls, Is.EqualTo(1));
            Assert.That(((AccelerometerStartResultEvent)device.Events[0]).IsSuccess, Is.True);
        }

        [Test] public void StartDuringStop_RestartsAfterStopInsteadOfRejectingAsBusy()
        {
            var device = new Device(); device.Start(); device.Started(true, null);
            device.Stop(); device.Start();
            Assert.That(device.StartCalls, Is.EqualTo(1));
            device.Stopped(true, null);
            Assert.That(device.StartCalls, Is.EqualTo(2));
            device.Started(true, null);
            Assert.That(device.AttachCalls, Is.EqualTo(2));
            Assert.That(((AccelerometerStartResultEvent)device.Events[device.Events.Count - 1]).IsSuccess, Is.True);
        }

        [Test] public void ForegroundDuringStop_RestartsOnlyAfterStopResult()
        {
            var device = new Device(); device.Start(); device.Started(true, null);
            device.Visible(false); device.Visible(true);
            Assert.That(device.StartCalls, Is.EqualTo(1));
            device.Stopped(true, null);
            Assert.That(device.StartCalls, Is.EqualTo(2));
        }

        [Test] public void HideDuringStart_WaitsForStartResultBeforeStopping()
        {
            var device = new Device(); device.Start(); device.Visible(false);
            Assert.That(device.StopCalls, Is.EqualTo(0));
            device.Started(true, null);
            Assert.That(device.AttachCalls, Is.EqualTo(0));
            Assert.That(device.StopCalls, Is.EqualTo(1));
        }

        [Test] public void OldSampleAndDuplicateStart_DoNotEnterRestartedStream()
        {
            var device = new Device(); device.Start();
            var oldStart = device.Started; oldStart(true, null);
            var oldOperation = device.ListeningOperation;
            device.Stop(); device.Stopped(true, null);
            device.Start(); device.Started(true, null);
            var count = device.Events.Count;
            device.Sample(oldOperation); oldStart(true, null);
            Assert.That(device.Events.Count, Is.EqualTo(count));
            device.Sample(device.ListeningOperation);
            Assert.That(device.Events[device.Events.Count - 1] is AccelerometerSampleEvent, Is.True);
        }

        [Test] public void ShutdownDuringStart_LateSuccessOnlyCleansUpDevice()
        {
            var device = new Device(); device.Start(); device.Shutdown();
            device.Started(true, null); device.Stopped(true, null);
            Assert.That(device.AttachCalls, Is.EqualTo(0));
            Assert.That(device.Events, Is.Empty);
            Assert.Throws<ObjectDisposedException>(() => device.Start());
        }

        [Test] public void StopFailure_IsObservableAndDoesNotAutomaticallyRestart()
        {
            var device = new Device(); device.Start(); device.Started(true, null);
            device.Stop(); device.Start(); device.Stopped(false, "SDK error");
            Assert.That(device.Failures, Is.EqualTo(1));
            Assert.That(device.StartCalls, Is.EqualTo(1));
            Assert.That(((AccelerometerStopResultEvent)device.Events[device.Events.Count - 1]).IsSuccess, Is.False);
            device.Start();
            Assert.That(((AccelerometerStartResultEvent)device.Events[device.Events.Count - 1]).IsSuccess, Is.False);
        }

        [Test] public void StartFailure_DoesNotRetryOnForegroundButAllowsExplicitStart()
        {
            var device = new Device(); device.Start(); device.Started(false, "SDK error");
            device.Visible(false); device.Visible(true);
            Assert.That(device.StartCalls, Is.EqualTo(1));
            Assert.That(((AccelerometerStartResultEvent)device.Events[0]).IsSuccess, Is.False);
            device.Start();
            Assert.That(device.StartCalls, Is.EqualTo(2));
        }

        [Test] public void RepeatedRequests_DoNotDuplicateSdkOperationsOrAcceptStoppedSamples()
        {
            var device = new Device(); device.Start(); device.Start(); device.Started(true, null); device.Start();
            Assert.That(device.StartCalls, Is.EqualTo(1));
            device.Stop(); device.Stop();
            Assert.That(device.StopCalls, Is.EqualTo(1));
            var count = device.Events.Count;
            device.Sample(device.ListeningOperation);
            Assert.That(device.Events.Count, Is.EqualTo(count));
        }
    }
}
