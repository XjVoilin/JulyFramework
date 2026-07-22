namespace July.Build
{
    public interface IBuildHost
    {
        bool Confirm(BuildContext context, int stepCount);
        void SaveAssets();
        void RefreshAssets();
        void ShowProgress(string stepName, int stepIndex, int stepCount);
        void ClearProgress();
        void Log(string message);
        void LogError(string message);
    }
}
