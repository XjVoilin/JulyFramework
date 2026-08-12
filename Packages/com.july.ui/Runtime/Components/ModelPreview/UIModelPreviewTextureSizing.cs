using System;
using UnityEngine;

namespace July.UI
{
    internal static class UIModelPreviewTextureSizing
    {
        private const float MinimumPixelsPerCanvasUnit = 1f;
        private const float MaximumResolutionCompensation = 2f;
        private const int MaximumPixelCount = 1024 * 1024;

        public static Vector2Int Calculate(
            Vector2 logicalSize,
            float canvasScaleFactor,
            int maximumTextureSize)
        {
            if (!IsPositiveFinite(logicalSize.x) ||
                !IsPositiveFinite(logicalSize.y) ||
                maximumTextureSize <= 0)
            {
                return Vector2Int.zero;
            }

            var safeCanvasScale = IsPositiveFinite(canvasScaleFactor)
                ? canvasScaleFactor
                : 1f;
            var resolutionCompensation = Mathf.Clamp(
                MinimumPixelsPerCanvasUnit / safeCanvasScale,
                1f,
                MaximumResolutionCompensation);
            var effectiveScale = safeCanvasScale * resolutionCompensation;

            var size = new Vector2Int(
                Mathf.Clamp(
                    Mathf.CeilToInt(logicalSize.x * effectiveScale),
                    1,
                    maximumTextureSize),
                Mathf.Clamp(
                    Mathf.CeilToInt(logicalSize.y * effectiveScale),
                    1,
                    maximumTextureSize));

            return LimitPixelCount(size);
        }

        private static Vector2Int LimitPixelCount(Vector2Int size)
        {
            var pixelCount = (long)size.x * size.y;
            if (pixelCount <= MaximumPixelCount)
                return size;

            var reduction = Math.Sqrt((double)MaximumPixelCount / pixelCount);
            return new Vector2Int(
                Math.Max(1, (int)Math.Floor(size.x * reduction)),
                Math.Max(1, (int)Math.Floor(size.y * reduction)));
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
