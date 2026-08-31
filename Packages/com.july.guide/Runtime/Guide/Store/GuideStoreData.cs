using System;
using System.Collections.Generic;

namespace July.Guide
{
    [Serializable]
    public sealed class GuideStoreData
    {
        public List<GuideDefinition> Guides = new();
        public List<GuideStepDefinition> Steps = new();
        public GuideProgressData Progress = new();
    }
}
