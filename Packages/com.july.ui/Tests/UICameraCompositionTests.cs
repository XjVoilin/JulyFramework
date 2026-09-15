using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace July.UI.Tests
{
    public sealed class UICameraCompositionTests
    {
        private Camera _main, _ui, _other;
        private UICameraComposition _composition;
        [SetUp] public void SetUp()
        {
            _main = new GameObject("TestMain").AddComponent<Camera>();
            _ui = new GameObject("TestUI").AddComponent<Camera>();
            _other = new GameObject("TestOverlay").AddComponent<Camera>();
            _other.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
            _main.GetUniversalAdditionalCameraData().cameraStack.Add(_other);
            _composition = new UICameraComposition(_ui);
        }
        [TearDown] public void TearDown()
        {
            _composition.Dispose();
            UnityEngine.Object.DestroyImmediate(_main.gameObject);
            UnityEngine.Object.DestroyImmediate(_ui.gameObject);
            UnityEngine.Object.DestroyImmediate(_other.gameObject);
        }
        [Test] public void BindingPreservesOverlaysAndDoesNotDuplicateUI()
        {
            _composition.Bind(_main);
            _composition.Bind(_main);
            CollectionAssert.AreEqual(new[] { _other, _ui }, _main.GetUniversalAdditionalCameraData().cameraStack);
            Assert.AreEqual(CameraRenderType.Overlay, _ui.GetUniversalAdditionalCameraData().renderType);
        }
        [Test] public void TransitionAndRecoveryPreserveOtherOverlays()
        {
            _composition.Bind(_main);
            _composition.ShowStandalone();
            CollectionAssert.AreEqual(new[] { _other }, _main.GetUniversalAdditionalCameraData().cameraStack);
            Assert.AreEqual(CameraRenderType.Base, _ui.GetUniversalAdditionalCameraData().renderType);
            _composition.Bind(_main);
            Assert.AreEqual(CameraRenderType.Overlay, _ui.GetUniversalAdditionalCameraData().renderType);
        }
        [Test] public void NoSceneCameraKeepsUIIndependent()
        {
            _composition.Bind(_main);
            _composition.Bind(null);
            Assert.AreEqual(CameraRenderType.Base, _ui.GetUniversalAdditionalCameraData().renderType);
            CollectionAssert.AreEqual(new[] { _other }, _main.GetUniversalAdditionalCameraData().cameraStack);
        }
        [Test] public void RejectsOverlayAsSceneOutput()
        {
            Assert.Throws<InvalidOperationException>(() => _composition.Bind(_other));
            Assert.AreEqual(CameraRenderType.Base, _ui.GetUniversalAdditionalCameraData().renderType);
        }
        [Test] public void DisposeDetachesOnlyOwnedCamera()
        {
            _composition.Bind(_main);
            _composition.Dispose();
            CollectionAssert.AreEqual(new[] { _other }, _main.GetUniversalAdditionalCameraData().cameraStack);
        }

        [Test] public void DisposeAfterUICameraWasDestroyedDoesNotThrow()
        {
            _composition.Bind(_main);
            UnityEngine.Object.DestroyImmediate(_ui.gameObject);
            _ui = null;

            Assert.DoesNotThrow(() => _composition.Dispose());
        }
    }
}
