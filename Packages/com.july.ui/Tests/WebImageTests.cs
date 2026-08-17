using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class WebImageTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.DestroyImmediate(_root);
        }

        [Test]
        public void Clear_BeforeAwake_DoesNotThrow()
        {
            var webImage = CreateInactiveWebImage();

            Assert.DoesNotThrow(webImage.Clear);
        }

        [Test]
        public void LoadEmptyUrl_BeforeAwake_DoesNotThrow()
        {
            var webImage = CreateInactiveWebImage();

            Assert.DoesNotThrow(() => webImage.Load(string.Empty));
        }

        private WebImage CreateInactiveWebImage()
        {
            _root = new GameObject(
                "WebImage",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            _root.SetActive(false);
            return _root.AddComponent<WebImage>();
        }
    }
}
