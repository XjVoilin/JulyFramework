using System;

namespace July.Guide
{
    public enum GuideExitReason { Completed, Skipped, Aborted, Faulted }

    public readonly struct GuideStartedEvent
    {
        public readonly int GuideId;
        public GuideStartedEvent(int guideId) => GuideId = guideId;
    }

    public readonly struct GuideExitedEvent
    {
        public readonly int GuideId;
        public readonly GuideExitReason Reason;
        public readonly Exception Error;
        public GuideExitedEvent(int guideId, GuideExitReason reason, Exception error = null)
        { GuideId = guideId; Reason = reason; Error = error; }
    }

    public readonly struct GuideStepEnteredEvent
    {
        public readonly int GuideId;
        public readonly int StepId;
        public GuideStepEnteredEvent(int guideId, int stepId) { GuideId = guideId; StepId = stepId; }
    }

    public readonly struct GuideStepExitedEvent
    {
        public readonly int GuideId;
        public readonly int StepId;
        public readonly GuideExitReason Reason;
        public GuideStepExitedEvent(int guideId, int stepId, GuideExitReason reason)
        { GuideId = guideId; StepId = stepId; Reason = reason; }
    }
}
