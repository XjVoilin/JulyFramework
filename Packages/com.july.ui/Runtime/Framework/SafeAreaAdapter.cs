using System;
using UnityEngine;
using UnityEngine.UI;

namespace July.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaAdapter : MonoBehaviour
    {
        /// <summary>
        /// Platform systems can set this to override Screen.safeArea (e.g. from WeChat/TikTok SDK).
        /// </summary>
        public static Func<Rect> SafeAreaOverride;

        private RectTransform _rect;
        private CanvasScaler _canvasScaler;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _canvasScaler = GetComponentInParent<CanvasScaler>();
        }

        private void OnEnable()
        {
            ApplySafeArea();
            ApplyCanvasScalerMatch();
        }

        private void ApplySafeArea()
        {
            var safeArea = SafeAreaOverride?.Invoke() ?? Screen.safeArea;

            float sw = Screen.width;
            float sh = Screen.height;
            if (sw <= 0 || sh <= 0) return;

            _rect.anchorMin = new Vector2(safeArea.x / sw, safeArea.y / sh);
            _rect.anchorMax = new Vector2(safeArea.xMax / sw, safeArea.yMax / sh);
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }

        private void ApplyCanvasScalerMatch()
        {
            if (_canvasScaler == null) return;

            float sw = Screen.width;
            float sh = Screen.height;
            if (sw <= 0 || sh <= 0) return;

            var refRes = _canvasScaler.referenceResolution;
            if (refRes.x <= 0 || refRes.y <= 0) return;

            var screenAspect = sw / sh;
            var designAspect = refRes.x / refRes.y;
            _canvasScaler.matchWidthOrHeight = screenAspect > designAspect ? 1f : 0f;
        }
    }
}
