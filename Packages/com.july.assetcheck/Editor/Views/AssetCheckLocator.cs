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
            Locate(path, current ? issue.ObjectLocation : null, issue.LocalId);
        }

        internal static void Locate(string path, AssetCheckObjectLocation location, long localId = 0)
        {
            Object target = null;
            if (location != null)
            {
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    var stage = PrefabStageUtility.OpenPrefab(path);
                    if (stage != null) target = location.Resolve(new[] { stage.prefabContentsRoot });
                }
                else
                {
                    var scene = SceneManager.GetSceneByPath(path);
                    if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    target = location.Resolve(scene.GetRootGameObjects());
                }
            }
            if (target == null && localId != 0) target = FindSubAsset(path, localId);
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
