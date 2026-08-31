using System;
using UnityEngine;

namespace July.Guide
{
    [RequireComponent(typeof(Renderer))]
    public sealed class GuideWorldTarget : GuideTargetAnchor
    {
        private Renderer _renderer;

        public Bounds Bounds => _renderer.bounds;

        public override Rect ScreenRect
        {
            get
            {
                var camera = GetSystem<GuideSystemBase>().WorldCamera;
                if (camera == null)
                    throw new InvalidOperationException("World camera is not registered.");
                return CalculateRect(camera, Bounds);
            }
        }

        protected override void OnViewAwake() => _renderer = GetComponent<Renderer>();

        private Rect CalculateRect(Camera camera, Bounds bounds)
        {
            var center = bounds.center;
            var extents = bounds.extents;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var point = camera.WorldToScreenPoint(center + Vector3.Scale(extents, new Vector3(x, y, z)));
                if (point.z <= 0)
                    throw new InvalidOperationException($"World guide target {name} is behind the registered camera.");
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
