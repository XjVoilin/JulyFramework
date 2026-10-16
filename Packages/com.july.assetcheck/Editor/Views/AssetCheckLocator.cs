using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace July.AssetCheck
{
    internal static class AssetCheckLocator
    {
        internal static Object FindSubAsset(string path, long localId) => AssetDatabase.LoadAllAssetsAtPath(path)
            .FirstOrDefault(asset => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long id) && id == localId);

        internal static void Locate(AssetCheckIssue issue, bool current)
        {
            var path = AssetCheckTarget.Resolve(issue.TargetId);
            Object target = null;
            if (current && issue.ObjectLocation != null)
            {
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    var stage = PrefabStageUtility.OpenPrefab(path);
                    if (stage != null) target = issue.ObjectLocation.Resolve(new[] { stage.prefabContentsRoot });
                }
                else
                {
                    var scene = SceneManager.GetSceneByPath(path);
                    if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    target = issue.ObjectLocation.Resolve(scene.GetRootGameObjects());
                }
            }
            if (target == null && issue.LocalId != 0) target = FindSubAsset(path, issue.LocalId);
            if (target == null) { Locate(path); return; }
            Select(target);
        }

        internal static void Locate(string path)
        {
            if (AssetCheckTarget.IsProjectFile(path))
            {
                var settingsPath = path.EndsWith("QualitySettings.asset", StringComparison.Ordinal) ? "Project/Quality" :
                    path.EndsWith("GraphicsSettings.asset", StringComparison.Ordinal) ? "Project/Graphics" : "Project/Player";
                SettingsService.OpenProjectSettings(settingsPath);
                return;
            }
            Select(AssetDatabase.LoadMainAssetAtPath(path));
        }

        private static void Select(Object target)
        {
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }
    }
}
