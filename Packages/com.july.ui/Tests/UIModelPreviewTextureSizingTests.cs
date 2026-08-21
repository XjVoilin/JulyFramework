using NUnit.Framework;
using UnityEngine;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class UIModelPreviewTextureSizingTests
    {
        [Test]
        public void Calculate_CompensatesLowCanvasScaleFromDeviceDiagnostics()
        {
            var size = UIModelPreviewTextureSizing.Calculate(
                new Vector2(518f, 653.2f),
                0.537f,
                1f,
                8192);

            Assert.That(size, Is.EqualTo(new Vector2Int(518, 654)));
        }

        [TestCase(0.8f, 500, 400)]
        [TestCase(1f, 500, 400)]
        [TestCase(1.5f, 750, 600)]
        public void Calculate_PreservesAtLeastOnePixelPerCanvasUnit(
            float canvasScale,
            int expectedWidth,
            int expectedHeight)
        {
            var size = UIModelPreviewTextureSizing.Calculate(
                new Vector2(500f, 400f),
                canvasScale,
                1f,
                8192);

            Assert.That(size, Is.EqualTo(new Vector2Int(expectedWidth, expectedHeight)));
        }

        [Test]
        public void Calculate_CapsCompensationForVeryLowCanvasScale()
        {
            var size = UIModelPreviewTextureSizing.Calculate(
                new Vector2(500f, 400f),
                0.25f,
                1f,
                8192);

            Assert.That(size, Is.EqualTo(new Vector2Int(250, 200)));
        }

        [Test]
        public void Calculate_CapsTotalPixelCount()
        {
            var size = UIModelPreviewTextureSizing.Calculate(
                new Vector2(1000f, 1000f),
                2f,
                1f,
                8192);

            Assert.That(size, Is.EqualTo(new Vector2Int(1024, 1024)));
        }

        [TestCase(0f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Calculate_UsesNeutralScaleForInvalidCanvasScale(float canvasScale)
        {
            var size = UIModelPreviewTextureSizing.Calculate(
                new Vector2(500f, 400f),
                canvasScale,
                1f,
                8192);

            Assert.That(size, Is.EqualTo(new Vector2Int(500, 400)));
        }

        [Test]
        public void Calculate_AppliesRenderTextureScale()
        {
            var size = UIModelPreviewTextureSizing.Calculate(
                new Vector2(500f, 400f),
                0.537f,
                0.7f,
                8192);

            Assert.That(size, Is.EqualTo(new Vector2Int(350, 280)));
        }
    }
}
