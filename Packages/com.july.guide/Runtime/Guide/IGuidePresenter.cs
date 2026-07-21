namespace July.Guide
{
    /// <summary>
    /// UI callback interface for guide presentation.
    /// </summary>
    public interface IGuidePresenter
    {
        void OnFlowStart(string flowId);
        void OnFlowComplete(string flowId);
        void OnStepEnter(GuideStepData stepData);
        void OnStepExit(GuideStepData stepData);
    }
}
