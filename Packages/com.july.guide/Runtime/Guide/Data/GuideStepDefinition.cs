using System;

namespace July.Guide
{
    [Serializable]
    public sealed class GuideStepDefinition
    {
        public int Id;
        public int GuideId;
        public int NextStepId;
        public int TargetId;
        public GuideHandlerRef[] Actions = Array.Empty<GuideHandlerRef>();
        public GuideViewData View = new();
        public int InputMode;
        public GuideHandlerRef Completion;
        public GuideHandlerRef[] ExpireConditions = Array.Empty<GuideHandlerRef>();
    }
}
