using System;
using UnityEngine;

namespace July.Guide
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class GuideUITarget : GuideTargetAnchor
    {
        private RectTransform _rectTransform;
        private Canvas _rootCanvas;
        private readonly Vector3[] _corners = new Vector3[4];

        public override Rect ScreenRect
        {
            get
            {
                var camera = _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : _rootCanvas.worldCamera;
                if (_rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay && camera == null)
                    throw new InvalidOperationException($"Guide UI target {name} requires its Canvas camera.");

                _rectTransform.GetWorldCorners(_corners);
                var min = RectTransformUtility.WorldToScreenPoint(camera, _corners[0]);
                var max = min;
                for (var i = 1; i < _corners.Length; i++)
                {
                    var point = RectTransformUtility.WorldToScreenPoint(camera, _corners[i]);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }

        protected override void OnViewAwake() => _rectTransform = (RectTransform)transform;

        protected override void OnViewEnable()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                throw new InvalidOperationException($"Guide UI target {name} must be under a Canvas.");
            _rootCanvas = canvas.rootCanvas;
            base.OnViewEnable();
        }
    }
}
