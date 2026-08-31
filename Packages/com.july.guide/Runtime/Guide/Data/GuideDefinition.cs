using System;

namespace July.Guide
{
    [Serializable]
    public sealed class GuideDefinition
    {
        public int Id;
        public int Priority;
        public int EntryStepId;
        public bool CanSkip;
        public GuideHandlerRef[] StartConditions = Array.Empty<GuideHandlerRef>();
        public GuideHandlerRef[] ExpireConditions = Array.Empty<GuideHandlerRef>();
    }
}
