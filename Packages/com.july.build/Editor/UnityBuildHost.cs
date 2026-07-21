using UnityEditor;
using UnityEngine;

namespace July.Build
{
    public sealed class UnityBuildHost : IBuildHost
    {
        public bool Confirm(BuildContext context, int stepCount) => EditorUtility.DisplayDialog(
            "确认构建",
            $"目标: {context.Target}\n平台: {context.Platform}\n环境: {context.Environment}\n步骤: {stepCount}",
            "构建", "取消");

        public void SaveAssets() => AssetDatabase.SaveAssets();
        public void ShowProgress(string stepName, int stepIndex, int stepCount) =>
            EditorUtility.DisplayProgressBar("July Build", stepName,
                stepCount == 0 ? 1f : (float)(stepIndex - 1) / stepCount);
        public void ClearProgress() => EditorUtility.ClearProgressBar();
        public void Log(string message) => Debug.Log(message);
        public void LogError(string message) => Debug.LogError(message);
    }
}
