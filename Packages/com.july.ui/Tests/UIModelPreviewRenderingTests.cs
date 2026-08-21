using NUnit.Framework;
using UnityEngine;

namespace July.UI.Tests
{
    [TestFixture]
    public sealed class UIModelPreviewRenderingTests
    {
        [Test]
        public void RenderTextureDescriptor_UsesSixteenBitDepth()
        {
            var descriptor = UIModelPreview.CreateRenderTextureDescriptor(
                new Vector2Int(512, 512),
                (int)ModelPreviewAntiAliasing.Disabled);

            Assert.That(descriptor.depthBufferBits, Is.EqualTo(16));
            Assert.That(descriptor.msaaSamples, Is.EqualTo(1));
        }

        [Test]
        public void PreviewCamera_UsesFocusedClippingRange()
        {
            var cameraObject = new GameObject("UIModelPreview Camera Test");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();

                UIModelPreview.ConfigurePreviewCamera(camera);

                Assert.That(camera.orthographic, Is.True);
                Assert.That(camera.nearClipPlane, Is.EqualTo(1f));
                Assert.That(camera.farClipPlane, Is.EqualTo(20f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
