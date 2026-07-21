namespace July.Guide
{
    /// <summary>
    /// Config query interface for guide flow/step definitions.
    /// </summary>
    public interface IGuideDataProvider
    {
        GuideFlowData GetFlow(string flowId);
        GuideStepData GetStep(string flowId, string stepId);
    }
}
