using UnityEditor;

namespace July.AssetCheck
{
    internal sealed class MeshSubMeshRule : IAssetCheckRule<ModelCheckContext>
    {
        public void Check(ModelCheckContext context)
        {
            foreach (var mesh in context.Meshes)
            {
                if (mesh.subMeshCount <= context.Standards.maxSubMeshes) continue;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId);
                context.Add(new AssetCheckIssue(context.AssetPath, guid, AssetCheckKind.Model, "model.mesh-submeshes",
                    "Mesh 子网格数", mesh.subMeshCount.ToString(), "≤ " + context.Standards.maxSubMeshes, false,
                    resourceName: context.AssetPath + " / " + mesh.name, localId: localId));
            }
        }
    }
}
