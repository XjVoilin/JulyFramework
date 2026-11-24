using System.Collections.Generic;
using UnityEngine.Scripting;

namespace July.Features
{
    [Preserve]
    internal sealed class FeatureConfig
    {
        [Preserve] public int id { get; set; } = -1;
        [Preserve] public List<FeatureConditionConfig> conditions { get; set; }
        [Preserve] public bool negate { get; set; }
    }

    [Preserve]
    internal sealed class FeatureConditionConfig
    {
        [Preserve] public int type { get; set; } = -1;
        [Preserve] public string arg { get; set; }
    }
}
