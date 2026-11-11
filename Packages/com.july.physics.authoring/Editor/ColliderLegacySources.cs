using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    // Old manual tools left disabled source colliders beside their final meshes.
    // Ignore those sources when inheriting output settings; never edit the recipe or mesh.
    internal static class ColliderLegacySources
    {
        internal static bool IsRetained(Collider collider)
        {
            if (collider.enabled) return false;
            return (collider is BoxCollider || collider is SphereCollider) &&
                collider.GetComponents<MeshCollider>().Any(IsEditedMesh);
        }

        static bool IsEditedMesh(MeshCollider collider)
        {
            var mesh = collider.sharedMesh;
            if (mesh == null) return false;
            var labels = AssetDatabase.GetLabels(mesh);
            return labels.Contains("FineColliderSharedPart") ||
                mesh.name == "g110_collider_hemisphere" || mesh.name == "g110_collider_cylinder" ||
                (collider.convex && (mesh.name.StartsWith("FineColliderPointMesh_", StringComparison.Ordinal) ||
                    mesh.name.StartsWith("FineSphereEdited_", StringComparison.Ordinal) ||
                    labels.Contains("FineColliderBoxMesh") || labels.Contains("FineColliderSphereMesh")));
        }
    }
}
