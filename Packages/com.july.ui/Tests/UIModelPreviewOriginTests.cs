using NUnit.Framework;
using UnityEngine;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class UIModelPreviewOriginTests
    {
        [Test]
        public void CalculateModelScale_PreservesReferenceScaleRatio()
        {
            var scale = UIModelPreview.CalculateModelScale(
                new Vector3(2f, 3f, 4f),
                1.5f);

            Assert.That(scale, Is.EqualTo(new Vector3(3f, 4.5f, 6f)));
        }

        [Test]
        public void CalculateModelOrigin_UsesVerticalAnchorAndOffset()
        {
            var origin = UIModelPreview.CalculateModelOrigin(
                new Rect(10f, 20f, 400f, 300f),
                0f,
                32f,
                220f,
                0,
                1);

            Assert.That(origin, Is.EqualTo(new Vector2(210f, 52f)));
        }

        [TestCase(0, -10f)]
        [TestCase(1, 210f)]
        [TestCase(2, 430f)]
        public void CalculateModelOrigin_CentersMultipleModelsWithEqualSpacing(
            int index,
            float expectedX)
        {
            var origin = UIModelPreview.CalculateModelOrigin(
                new Rect(10f, 20f, 400f, 300f),
                0f,
                32f,
                220f,
                index,
                3);

            Assert.That(origin.x, Is.EqualTo(expectedX));
            Assert.That(origin.y, Is.EqualTo(52f));
        }
    }
}
