using System;
using System.Collections.Generic;

namespace July.Guide
{
    [Serializable]
    public sealed class GuideProgressData
    {
        public int CurrentGuideId;
        public int CurrentStepId;
        public List<int> CompletedGuideIds = new();
        public List<int> CompletedStepIds = new();
    }
}

