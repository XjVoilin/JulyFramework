using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace July.UI.Tests
{
    public sealed class UIShaderTimeTests
    {
        private static readonly int ClockId = Shader.PropertyToID("_JulyUIUnscaledTime");
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = UnityEngine.Time.timeScale;
            UIShaderTime.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Time.timeScale = _timeScale;
            UIShaderTime.Initialize();
        }

        [Test]
        public void ReinitializeAndShutdown_ManageCanvasSubscription()
        {
            UIShaderTime.Initialize();
            Shader.SetGlobalFloat(ClockId, -1f);
            Canvas.ForceUpdateCanvases();
            Assert.That(Shader.GetGlobalFloat(ClockId), Is.EqualTo(UnityEngine.Time.unscaledTime));

            UIShaderTime.Shutdown();
            Shader.SetGlobalFloat(ClockId, -1f);
            Canvas.ForceUpdateCanvases();
            Assert.That(Shader.GetGlobalFloat(ClockId), Is.EqualTo(-1f));

            UIShaderTime.Initialize();
            Assert.That(Shader.GetGlobalFloat(ClockId), Is.EqualTo(UnityEngine.Time.unscaledTime));
        }

        [UnityTest]
        public IEnumerator CanvasClock_ContinuesWhileTimeScaleIsZero()
        {
            UnityEngine.Time.timeScale = 0f;
            // Start on a frame whose scaled delta is already zero.
            yield return null;
            Canvas.ForceUpdateCanvases();
            var before = Shader.GetGlobalFloat(ClockId);
            var scaledBefore = UnityEngine.Time.time;

            yield return new WaitForSecondsRealtime(0.05f);
            Canvas.ForceUpdateCanvases();

            Assert.That(UnityEngine.Time.time, Is.EqualTo(scaledBefore));
            Assert.That(Shader.GetGlobalFloat(ClockId), Is.GreaterThan(before));
            Assert.That(Shader.GetGlobalFloat(ClockId), Is.EqualTo(UnityEngine.Time.unscaledTime));
        }
    }
}
