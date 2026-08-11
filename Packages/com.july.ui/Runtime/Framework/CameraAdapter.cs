using UnityEngine;

namespace July.UI
{
    [RequireComponent(typeof(Camera))]
    public class CameraAdapter : MonoBehaviour
    {
        private Camera _cam;
        private float _designOrthoSize;
        private Vector2 _designResolution = new(1080, 1920);

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _designOrthoSize = _cam.orthographicSize;
        }

        private void OnEnable() => Apply();

        private void Apply()
        {
            if (_cam == null || !_cam.orthographic) return;
            if (_designResolution.x <= 0 || _designResolution.y <= 0) return;

            float sw = Screen.width;
            float sh = Screen.height;
            if (sw <= 0 || sh <= 0) return;

            var screenAspect = sw / sh;
            var designAspect = _designResolution.x / _designResolution.y;

            _cam.orthographicSize = screenAspect >= designAspect
                ? _designOrthoSize
                : _designOrthoSize * designAspect / screenAspect;
        }
    }
}
