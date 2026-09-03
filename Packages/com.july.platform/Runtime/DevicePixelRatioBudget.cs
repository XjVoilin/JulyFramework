using System;

namespace July.Platform
{
    internal static class DevicePixelRatioBudget
    {
        private const double MinimumDpr = 1d;

        public static double Limit(
            double performanceDpr,
            double logicalWidth,
            double logicalHeight,
            int maxFramebufferLongEdge)
        {
            if (!IsPositiveFinite(performanceDpr))
                throw new ArgumentOutOfRangeException(nameof(performanceDpr));
            if (!IsPositiveFinite(logicalWidth))
                throw new ArgumentOutOfRangeException(nameof(logicalWidth));
            if (!IsPositiveFinite(logicalHeight))
                throw new ArgumentOutOfRangeException(nameof(logicalHeight));
            if (maxFramebufferLongEdge <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxFramebufferLongEdge));

            var longEdgeDpr = maxFramebufferLongEdge /
                              Math.Max(logicalWidth, logicalHeight);
            return Math.Max(
                MinimumDpr,
                Math.Min(performanceDpr, longEdgeDpr));
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
