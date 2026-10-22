using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring
{
    internal static class MeshAssetEdits
    {
        internal static string Identity(UMesh mesh)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long id) || string.IsNullOrEmpty(guid))
                throw new ArgumentException("Mesh 必须有持久化资源来源。");
            return guid + "_" + id;
        }

        internal static bool Standalone(UMesh mesh) => mesh && AssetDatabase.IsMainAsset(mesh)
            && AssetDatabase.GetAssetPath(mesh).EndsWith(".asset", StringComparison.OrdinalIgnoreCase);

        internal static void EnsureFolder(string path)
        {
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        internal static void CheckColliderUse(UMesh source)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            foreach (var collider in Resources.FindObjectsOfTypeAll<MeshCollider>())
                if (collider.sharedMesh == source) throw new InvalidOperationException("此 Mesh 同时被 MeshCollider 使用，首版不原位修改：" + collider.name);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.GetDependencies(path, true).Contains(sourcePath)) continue;
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root.GetComponentsInChildren<MeshCollider>(true).Any(c => c.sharedMesh == source))
                        throw new InvalidOperationException("MeshCollider 引用此 Mesh，首版不原位修改：" + path);
                }
                else
                {
                    var previous = SceneManager.GetActiveScene();
                    var scene = SceneManager.GetSceneByPath(path);
                    bool opened = !scene.IsValid() || !scene.isLoaded;
                    if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    try
                    {
                        if (scene.GetRootGameObjects().Any(root => root.GetComponentsInChildren<MeshCollider>(true).Any(c => c.sharedMesh == source)))
                            throw new InvalidOperationException("MeshCollider 引用此 Mesh，首版不原位修改：" + path);
                    }
                    finally
                    {
                        if (opened) EditorSceneManager.CloseScene(scene, true);
                        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                    }
                }
            }
        }

    }
}
