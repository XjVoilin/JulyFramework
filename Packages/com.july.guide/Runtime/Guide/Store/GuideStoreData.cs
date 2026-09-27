using System;
using System.Collections.Generic;

namespace July.Guide
{
    /// <summary>Durable outcomes only; definitions and in-flight cursors are never saved.</summary>
    [Serializable]
    public sealed class GuideStoreData
    {
        public List<int> CompletedGuideIds = new();
        public List<int> SkippedGuideIds = new();
    }
}
