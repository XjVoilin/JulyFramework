using System;

namespace July.Platform
{
    internal enum DevicePerformanceTier
    {
        Low,
        Medium,
        High
    }

    internal static class DevicePixelRatioPolicy
    {
        private const double MinimumDpr = 1d;

        public static double Calculate(
            DprConfig config,
            DevicePerformanceTier tier,
            double defaultDpr,
            double logicalWidth,
            double logicalHeight)
        {
            if (!config.EnablePixelBudget)
                return defaultDpr;

            if (!IsPositiveFinite(defaultDpr))
                throw new ArgumentOutOfRangeException(nameof(defaultDpr));
            if (!IsPositiveFinite(logicalWidth))
                throw new ArgumentOutOfRangeException(nameof(logicalWidth));
            if (!IsPositiveFinite(logicalHeight))
                throw new ArgumentOutOfRangeException(nameof(logicalHeight));

            var maxPixels = GetPixelBudget(config, tier);
            if (maxPixels <= 0)
                throw new ArgumentOutOfRangeException(nameof(config), $"Pixel budget for {tier} must be positive.");
            var targetDpr = Math.Min(defaultDpr, Math.Sqrt(maxPixels / (logicalWidth * logicalHeight)));

            // Preserve the DPR 1 floor without increasing a smaller platform default.
            return Math.Min(defaultDpr, Math.Max(MinimumDpr, targetDpr));
        }

        private static int GetPixelBudget(DprConfig config, DevicePerformanceTier tier) 
        {
            switch (tier)
            {
                case DevicePerformanceTier.High: return config.HighTierPixels;
                case DevicePerformanceTier.Medium: return config.MediumTierPixels;
                case DevicePerformanceTier.Low: return config.LowTierPixels;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, null);
            }
        }

        private static bool IsPositiveFinite(double value)
        {
            return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
