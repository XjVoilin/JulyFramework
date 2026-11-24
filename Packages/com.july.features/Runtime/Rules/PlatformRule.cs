using System;
using System.Globalization;
using July.Platform;

namespace July.Features
{
    internal sealed class PlatformRule : IFeatureRule
    {
        /// <summary>内置平台规则的编号。项目条件枚举中的平台值与之对应。</summary>
        private const int PlatformRuleId = 1;

        private readonly IPlatformSystem _platform;

        public PlatformRule(IPlatformSystem platform) => _platform = platform;

        public int Id => PlatformRuleId;

        public bool IsSatisfied(string arg)
        {
            if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var platformType))
                throw new ArgumentException("平台编号必须是整数。", nameof(arg));
            return _platform.PlatformType == platformType;
        }
    }
}
