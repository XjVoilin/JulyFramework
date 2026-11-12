using System;
using System.Reflection;
using July.Release.Editor;
using NUnit.Framework;
using UnityEngine;

namespace July.Release.Tests
{
    public sealed class BuildWindowQueueTests
    {
        private BuildPipelinePanel _panel;
        private BuildToolContext _context;

        [SetUp]
        public void Setup()
        {
            _context = new BuildToolContext();
            _panel = new BuildPipelinePanel();
            _panel.OnEnable(_context);
        }

        [TearDown] public void Cleanup() => _panel.Dispose();

        [Test]
        public void QueueDoesNotRunInlineAndCallbackExecutesOnlyOnce()
        {
            var calls = 0;
            _panel.QueueBuild(() => calls++);
            Assert.That(calls, Is.Zero);
            Assert.That(_panel.IsBusy, Is.True);
            _panel.RunQueuedBuild();
            _panel.RunQueuedBuild();
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_panel.IsBusy, Is.False);
        }

        [Test]
        public void DuplicateRequestsAreRejectedWhileQueuedAndRunning()
        {
            var calls = 0;
            _panel.QueueBuild(() =>
            {
                calls++;
                Assert.That(_panel.IsBusy, Is.True);
                _panel.QueueBuild(() => calls += 100);
            });
            _panel.QueueBuild(() => calls += 10);
            _panel.RunQueuedBuild();
            Assert.That(calls, Is.EqualTo(1));
            _panel.QueueBuild(() => calls++);
            _panel.RunQueuedBuild();
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void CancellingPendingRequestMakesAlreadyTakenCallbackHarmless()
        {
            var calls = 0;
            _panel.QueueBuild(() => calls++);
            _panel.Dispose();
            _panel.Dispose();
            _panel.RunQueuedBuild();
            Assert.That(calls, Is.Zero);
            Assert.That(_panel.IsBusy, Is.False);
        }

        [Test]
        public void ExecutionFailurePropagatesAndReleasesBusyState()
        {
            var failure = new InvalidOperationException("build fixture");
            _panel.QueueBuild(() => throw failure);
            Assert.That(Assert.Throws<InvalidOperationException>(_panel.RunQueuedBuild), Is.SameAs(failure));
            Assert.That(_panel.IsBusy, Is.False);
            var calls = 0;
            _panel.QueueBuild(() => calls++);
            _panel.RunQueuedBuild();
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void WindowDisableCancelsPendingBuildAndDetachesRepaint()
        {
            var calls = 0;
            var window = ScriptableObject.CreateInstance<BuildToolWindow>();
            try
            {
                _context.Repaint = window.Repaint;
                typeof(BuildToolWindow).GetField("_ctx", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, _context);
                typeof(BuildToolWindow).GetField("_build", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, _panel);
                _panel.QueueBuild(() => calls++);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
            _panel.RunQueuedBuild();
            Assert.That(calls, Is.Zero);
            Assert.That(_context.Repaint, Is.Null);
        }
    }
}
