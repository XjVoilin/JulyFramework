using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace July.Launch.Editor
{
    [InitializeOnLoad]
    internal static class LaunchScenePlayer
    {
        private const string PendingKey = "July.Launch.Editor.Pending";
        private const string PreviousSceneKey = "July.Launch.Editor.PreviousScene";

        internal static bool CanPlay => !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorApplication.isCompiling && !EditorApplication.isUpdating
            && !SessionState.GetBool(PendingKey, false);

        static LaunchScenePlayer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += RestoreStartScene;
            if (SessionState.GetBool(PendingKey, false))
                EditorApplication.update += RestoreWhenIdle;
        }

        internal static void Play()
        {
            if (!CanPlay) return;

            var scenePaths = AssetDatabase.FindAssets("Launch t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), "Launch",
                    StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (scenePaths.Length != 1)
            {
                var message = scenePaths.Length == 0
                    ? "Assets 下未找到名称严格为 Launch 的场景。请创建或重命名启动场景为 Launch.unity。"
                    : "找到多个 Launch 场景，无法确定启动入口。请保留唯一的 Launch.unity：\n\n"
                        + string.Join("\n", scenePaths);
                EditorUtility.DisplayDialog("July Launch", message, "确定");
                return;
            }

            // 在覆盖启动设置之前处理保存/取消，不切换编辑中的场景。
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var previousScene = EditorSceneManager.playModeStartScene;
            SessionState.SetString(PreviousSceneKey, previousScene == null ? string.Empty
                : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(previousScene)));
            SessionState.SetBool(PendingKey, true);
            EditorApplication.update += RestoreWhenIdle;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePaths[0]);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("July/Launch/Play Launch Scene", true)]
        private static bool ValidatePlay() => CanPlay;

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                RestoreStartScene();
        }

        private static void RestoreWhenIdle()
        {
            // 包括编译失败、其它编辑器回调取消进入播放，以及空闲时脚本重载。
            if (!EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                RestoreStartScene();
        }

        private static void RestoreStartScene()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;

            var previousGuid = SessionState.GetString(PreviousSceneKey, string.Empty);
            var previousScene = string.IsNullOrEmpty(previousGuid) ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(previousGuid));
            if (!string.IsNullOrEmpty(previousGuid) && previousScene == null)
                Debug.LogWarning("[July Launch] 原播放启动场景已被删除，已清除临时启动设置。");
            EditorSceneManager.playModeStartScene = previousScene;
            SessionState.EraseBool(PendingKey);
            SessionState.EraseString(PreviousSceneKey);
            EditorApplication.update -= RestoreWhenIdle;
        }
    }
}
