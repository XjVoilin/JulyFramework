using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace July.UI
{
    // Owned by UISystem. Only the framework UI camera's stack entry is managed here.
    internal sealed class UICameraComposition : IDisposable
    {
        private readonly Camera _uiCamera;
        private readonly UniversalAdditionalCameraData _uiData;
        private Camera _sceneCamera;

        internal UICameraComposition(Camera uiCamera)
        {
            _uiCamera = uiCamera;
            _uiData = uiCamera.GetUniversalAdditionalCameraData();
            ShowStandalone();
        }

        internal void Rebind()
        {
            Camera selected = null;
            foreach (var camera in Camera.allCameras)
            {
                if (camera == _uiCamera || !camera.CompareTag("MainCamera")) continue;
                if (selected != null)
                    throw new InvalidOperationException("July.UI requires one active MainCamera; multiple cameras are tagged MainCamera.");
                selected = camera;
            }
            Bind(selected);
        }

        internal void Bind(Camera camera)
        {
            ShowStandalone();
            // No scene camera is a supported state for launch and UI-only scenes.
            if (camera == null) return;
            var data = camera.GetUniversalAdditionalCameraData();
            if (data.renderType != CameraRenderType.Base || camera.targetTexture != null ||
                camera.targetDisplay != _uiCamera.targetDisplay)
                throw new InvalidOperationException("July.UI MainCamera must be a Base camera rendering to the UI display without a RenderTexture.");
            var stack = data.cameraStack;
            if (stack == null)
                throw new InvalidOperationException("July.UI requires a URP renderer supporting camera stacking.");
            _sceneCamera = camera;
            _uiData.renderType = CameraRenderType.Overlay;

            // Preserve project overlays; framework windows render last.
            stack.Remove(_uiCamera);
            stack.Add(_uiCamera);
        }

        internal void BeforeUnload(string sceneName)
        {
            if (_sceneCamera != null && _sceneCamera.gameObject.scene.name == sceneName)
                ShowStandalone();
        }

        internal void ShowStandalone()
        {
            if (_sceneCamera != null)
                _sceneCamera.GetUniversalAdditionalCameraData().cameraStack.Remove(_uiCamera);
            _sceneCamera = null;
            _uiData.renderType = CameraRenderType.Base;
            _uiCamera.clearFlags = CameraClearFlags.SolidColor;
            _uiCamera.backgroundColor = Color.black;
        }

        public void Dispose() => ShowStandalone();
    }
}
