using System;
using System.IO;
using UnityEditor;
using UnityEngine;
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
            MeshColliderUsage.Scan(new[] { source }).Check(source);
        }
    }
}
