namespace July.Guide
{
    public static class GuideExitReasons
    {
        public const int Completed = 0;
        public const int Skipped = 1;
        public const int Expired = 2;
    }

    public readonly struct GuideStartedEvent
    {
        public readonly int GuideId;
        public GuideStartedEvent(int guideId) => GuideId = guideId;
    }

    public readonly struct GuideExitedEvent
    {
        public readonly int GuideId;
        public readonly int Reason;

        public GuideExitedEvent(int guideId, int reason)
        {
            GuideId = guideId;
            Reason = reason;
        }
    }

    public readonly struct GuideStepEnteredEvent
    {
        public readonly int GuideId;
        public readonly int StepId;

        public GuideStepEnteredEvent(int guideId, int stepId)
        {
            GuideId = guideId;
            StepId = stepId;
        }
    }

    public readonly struct GuideStepExitedEvent
    {
        public readonly int GuideId;
        public readonly int StepId;
        public readonly int Reason;

        public GuideStepExitedEvent(int guideId, int stepId, int reason)
        {
            GuideId = guideId;
            StepId = stepId;
            Reason = reason;
        }
    }
}
