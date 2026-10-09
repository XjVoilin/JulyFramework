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
            if (_ui != null) UnityEngine.Object.DestroyImmediate(_ui.gameObject);
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
            if (_ui != null) UnityEngine.Object.DestroyImmediate(_ui.gameObject);
            _ui = null;

            Assert.DoesNotThrow(() => _composition.Dispose());
        }
    }
}

#if UNITY_EDITOR
namespace July.UI.Tests
{
    public sealed class UICameraRendererSelectionTests
    {
        private UnityEngine.Rendering.RenderPipelineAsset _previousGraphicsPipeline;
        private UnityEngine.Rendering.RenderPipelineAsset _previousQualityPipeline;
        private UniversalRenderPipelineAsset _pipeline;
        private Renderer2DData _renderer2D;
        private UniversalRendererData _renderer3D;
        private Camera _main, _ui;
        private UICameraComposition _composition;

        [SetUp]
        public void SetUp()
        {
            _previousGraphicsPipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            _previousQualityPipeline = QualitySettings.renderPipeline;
            _renderer2D = ScriptableObject.CreateInstance<Renderer2DData>();
            _renderer3D = ScriptableObject.CreateInstance<UniversalRendererData>();
            _pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
            // Only test fixture asset setup uses editor serialization; runtime uses public URP APIs.
            var serialized = new UnityEditor.SerializedObject(_pipeline);
            var renderers = serialized.FindProperty("m_RendererDataList");
            renderers.arraySize = 2;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = _renderer2D;
            renderers.GetArrayElementAtIndex(1).objectReferenceValue = _renderer3D;
            serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = _pipeline;
            QualitySettings.renderPipeline = _pipeline;
            _main = new GameObject("RendererTestMain").AddComponent<Camera>();
            _ui = new GameObject("RendererTestUI").AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            _composition?.Dispose();
            UnityEngine.Object.DestroyImmediate(_main.gameObject);
            UnityEngine.Object.DestroyImmediate(_ui.gameObject);
            QualitySettings.renderPipeline = _previousQualityPipeline;
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = _previousGraphicsPipeline;
            UnityEngine.Object.DestroyImmediate(_pipeline);
            UnityEngine.Object.DestroyImmediate(_renderer2D);
            UnityEngine.Object.DestroyImmediate(_renderer3D);
        }

        [Test]
        public void NonDefaultRendererIsSharedAndLeavingRestoresDefault()
        {
            var scene = _main.GetUniversalAdditionalCameraData();
            scene.SetRenderer(1);
            _composition = new UICameraComposition(_ui, new[] { 1 });
            _composition.Bind(_main);
            var ui = _ui.GetUniversalAdditionalCameraData();
            Assert.That(ui.scriptableRenderer, Is.SameAs(scene.scriptableRenderer));
            Assert.That(ui.renderType, Is.EqualTo(CameraRenderType.Overlay));
            CollectionAssert.AreEqual(new[] { _ui }, scene.cameraStack);

            _composition.ShowStandalone();
            Assert.That(ui.scriptableRenderer, Is.SameAs(_pipeline.GetRenderer(-1)));
            Assert.That(ui.renderType, Is.EqualTo(CameraRenderType.Base));
            Assert.That(scene.cameraStack, Is.Empty);

            scene.SetRenderer(0);
            _composition.Bind(_main);
            Assert.That(ui.scriptableRenderer, Is.SameAs(scene.scriptableRenderer));
            CollectionAssert.AreEqual(new[] { _ui }, scene.cameraStack);
        }

        [Test]
        public void DefaultSceneRendererNeedsNoAdditionalConfiguration()
        {
            _composition = new UICameraComposition(_ui);
            _composition.Bind(_main);
            Assert.That(_ui.GetUniversalAdditionalCameraData().scriptableRenderer,
                Is.SameAs(_main.GetUniversalAdditionalCameraData().scriptableRenderer));
        }

        [Test]
        public void UnconfiguredSceneRendererFailsWithoutPartiallyBindingUI()
        {
            var scene = _main.GetUniversalAdditionalCameraData();
            scene.SetRenderer(1);
            _composition = new UICameraComposition(_ui);
            var exception = Assert.Throws<InvalidOperationException>(() => _composition.Bind(_main));
            StringAssert.Contains(nameof(UIConfig.AdditionalCameraRendererIndices), exception.Message);
            Assert.That(scene.cameraStack, Is.Empty);
            var ui = _ui.GetUniversalAdditionalCameraData();
            Assert.That(ui.renderType, Is.EqualTo(CameraRenderType.Base));
            Assert.That(ui.scriptableRenderer, Is.SameAs(_pipeline.GetRenderer(-1)));
        }

        [Test]
        public void ProjectConfigurationMutationDoesNotChangeActiveComposition()
        {
            var indices = new[] { 1 };
            _composition = new UICameraComposition(_ui, indices);
            indices[0] = 0;
            _main.GetUniversalAdditionalCameraData().SetRenderer(1);
            _composition.Bind(_main);
            Assert.That(_ui.GetUniversalAdditionalCameraData().scriptableRenderer,
                Is.SameAs(_main.GetUniversalAdditionalCameraData().scriptableRenderer));
        }

        [Test]
        public void NegativeAdditionalRendererIndexIsRejectedAtConfigurationBoundary()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new UICameraComposition(_ui, new[] { -1 }));
        }
    }
}
#endif
