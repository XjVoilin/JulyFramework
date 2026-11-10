using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UMesh = UnityEngine.Mesh;

namespace July.Mesh.Authoring
{
    // A snapshot for one synchronous batch; it must not be retained across operations.
    internal sealed class MeshColliderUsage
    {
        readonly Dictionary<UMesh, string> errors;
        readonly Dictionary<string, UMesh> remaining;
        internal bool Cancelled { get; private set; }

        MeshColliderUsage(IEnumerable<UMesh> sources)
        {
            errors = sources.Distinct().ToDictionary(source => source, source => (string)null);
            remaining = errors.Keys.ToDictionary(AssetDatabase.GetAssetPath, source => source);
        }

        internal static MeshColliderUsage Scan(IEnumerable<UMesh> sources, Func<string, float, bool> cancel = null)
        {
            var usage = new MeshColliderUsage(sources);
            usage.Read(cancel);
            return usage;
        }

        internal void Check(UMesh source)
        {
            string error = errors[source];
            if (error != null) throw new InvalidOperationException(error);
        }

        void Reject(UMesh source, string message)
        {
            if (errors[source] != null) return;
            errors[source] = message;
            remaining.Remove(AssetDatabase.GetAssetPath(source));
        }

        void CheckColliders(IEnumerable<MeshCollider> colliders, string message)
        {
            foreach (var collider in colliders)
                if (collider.sharedMesh && errors.ContainsKey(collider.sharedMesh))
                    Reject(collider.sharedMesh, message ?? "此 Mesh 同时被 MeshCollider 使用，首版不原位修改：" + collider.name);
        }

        void Read(Func<string, float, bool> cancel)
        {
            if (cancel != null && cancel("检查已加载对象", 0)) { Cancelled = true; return; }
            CheckColliders(Resources.FindObjectsOfTypeAll<MeshCollider>(), null);
            if (remaining.Count == 0) return;
            string[] references;
            try { references = AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" }); }
            catch (Exception error)
            {
                foreach (var source in remaining.Values.ToArray()) Reject(source, error.Message);
                return;
            }
            for (int i = 0; i < references.Length && remaining.Count > 0; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(references[i]);
                if (cancel != null && cancel(path, (float)i / references.Length)) { Cancelled = true; return; }
                UMesh[] affected;
                try
                {
                    affected = AssetDatabase.GetDependencies(path, true).Where(remaining.ContainsKey)
                        .Select(dependency => remaining[dependency]).Distinct().ToArray();
                }
                catch (Exception error)
                {
                    // Unknown dependencies prevent proving safety for any remaining target.
                    foreach (var source in remaining.Values.ToArray()) Reject(source, path + ": " + error.Message);
                    break;
                }
                if (affected.Length == 0) continue;
                try { ReadReference(path); }
                catch (Exception error)
                {
                    // A bad asset only fails the targets which depend on its inspection.
                    foreach (var source in affected) Reject(source, path + ": " + error.Message);
                }
            }
        }

        void ReadReference(string path)
        {
            string message = "MeshCollider 引用此 Mesh，首版不原位修改：" + path;
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                CheckColliders(root.GetComponentsInChildren<MeshCollider>(true), message);
                return;
            }
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                foreach (var root in scene.GetRootGameObjects())
                    CheckColliders(root.GetComponentsInChildren<MeshCollider>(true), message);
            }
            finally
            {
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
    }
}
