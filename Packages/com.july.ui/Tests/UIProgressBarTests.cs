using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class UIProgressBarTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.DestroyImmediate(_root);
        }

        [Test]
        public void SetValue_UpdatesRightPaddingAndPreservesAuthoredPadding()
        {
            var progressBar = CreateProgressBar(200f, 100f, new Vector4(5f, 2f, 7f, 3f));

            progressBar.SetValue(25f, 100f);

            Assert.That(progressBar.NormalizedValue, Is.EqualTo(0.25f));
            Assert.That(GetMask().padding, Is.EqualTo(new Vector4(5f, 2f, 148f, 3f)));
        }

        [TestCase(UIProgressDirection.LeftToRight, 1f, 2f, 75f, 4f)]
        [TestCase(UIProgressDirection.RightToLeft, 73f, 2f, 3f, 4f)]
        [TestCase(UIProgressDirection.BottomToTop, 1f, 2f, 3f, 59.5f)]
        [TestCase(UIProgressDirection.TopToBottom, 1f, 57.5f, 3f, 4f)]
        public void SetValue_AppliesConfiguredDirection(
            UIProgressDirection direction,
            float expectedLeft,
            float expectedBottom,
            float expectedRight,
            float expectedTop)
        {
            var progressBar = CreateProgressBar(100f, 80f, new Vector4(1f, 2f, 3f, 4f));
            SetDirection(progressBar, direction);

            progressBar.SetValue(1f, 4f);

            Assert.That(GetMask().padding, Is.EqualTo(new Vector4(
                expectedLeft,
                expectedBottom,
                expectedRight,
                expectedTop)));
        }

        [TestCase(-1f, 100f, 0f)]
        [TestCase(50f, 0f, 0f)]
        [TestCase(150f, 100f, 1f)]
        public void SetValue_ClampsInvalidOrOutOfRangeValues(
            float current,
            float maximum,
            float expected)
        {
            var progressBar = CreateProgressBar(100f, 100f, Vector4.zero);

            progressBar.SetValue(current, maximum);

            Assert.That(progressBar.NormalizedValue, Is.EqualTo(expected));
            Assert.That(GetMask().padding.z, Is.EqualTo(100f * (1f - expected)));
        }

        [Test]
        public void RectTransformResize_ReappliesCurrentValue()
        {
            var progressBar = CreateProgressBar(100f, 100f, Vector4.zero);
            progressBar.SetValue(1f, 2f);

            var rectTransform = _root.GetComponent<RectTransform>();
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 300f);
            progressBar.SendMessage("OnRectTransformDimensionsChange");

            Assert.That(GetMask().padding.z, Is.EqualTo(150f));
        }

        private UIProgressBar CreateProgressBar(float width, float height, Vector4 padding)
        {
            _root = new GameObject("UIProgressBar", typeof(RectTransform), typeof(RectMask2D));
            var rectTransform = _root.GetComponent<RectTransform>();
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            _root.GetComponent<RectMask2D>().padding = padding;
            return _root.AddComponent<UIProgressBar>();
        }

        private RectMask2D GetMask()
        {
            return _root.GetComponent<RectMask2D>();
        }

        private static void SetDirection(UIProgressBar progressBar, UIProgressDirection direction)
        {
            typeof(UIProgressBar)
                .GetField("_direction", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(progressBar, direction);
        }
    }
}
