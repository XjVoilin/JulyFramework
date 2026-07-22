using UnityEditor;
using UnityEngine;

namespace July.Build
{
    public sealed class UnityBuildHost : IBuildHost
    {
        public bool Confirm(BuildContext context, int stepCount) => EditorUtility.DisplayDialog(
            "Confirm Build",
            $"Target: {context.Target}\nPlatform: {context.Platform}\n" +
            $"Environment: {context.Environment}\nVersion: {context.Version}\nSteps: {stepCount}",
            "Build", "Cancel");

        public void SaveAssets() => AssetDatabase.SaveAssets();
        public void RefreshAssets() => AssetDatabase.Refresh();
        public void ShowProgress(string stepName, int stepIndex, int stepCount) =>
            EditorUtility.DisplayProgressBar("July Build", stepName,
                stepCount == 0 ? 1f : (float)(stepIndex - 1) / stepCount);
        public void ClearProgress() => EditorUtility.ClearProgressBar();
        public void Log(string message) => Debug.Log(message);
        public void LogError(string message) => Debug.LogError(message);
    }
}
