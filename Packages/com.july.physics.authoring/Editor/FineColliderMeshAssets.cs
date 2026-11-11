using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    internal static class FineColliderMeshAssets
    {
        const int GeometryVersion = 1;
        internal static void EnsureFolder(string folder)
        {
            ColliderAuthoringProfile.ValidateAssetDirectory(folder);
            string current = "Assets";
            foreach (var part in folder.Substring("Assets/".Length).Split('/'))
            {
                string next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
                current = next;
            }
        }
        // Immutable content-addressed assets keep Prefab/scene references valid across save, Undo and reload.
        // Unused meshes remain available after an Undo.
        internal static Mesh GetOrCreate(PrimitiveColliderFitter.Shape shape, string folder, out Vector3 scale)
        {
            var standard = SixPrimitiveMeshes.Standardize(shape, out scale);
            var vertices = standard.ConvexVertices;
            var triangles = standard.ConvexTriangles;
            string digest;
            using(var data=new MemoryStream()){
                using(var writer=new BinaryWriter(data,System.Text.Encoding.UTF8,true)){
                    writer.Write((int)shape.Type); writer.Write(GeometryVersion);
                    writer.Write(vertices.Length);
                    foreach(var p in vertices){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
                    writer.Write(triangles.Length);foreach(int index in triangles)writer.Write(index);
                }
                using var hash=SHA256.Create();
                digest=BitConverter.ToString(hash.ComputeHash(data.ToArray())).Replace("-","").ToLowerInvariant();
            }
            string name = shape.Type == PrimitiveColliderFitter.Kind.Cylinder ? $"Cylinder_S{SixPrimitiveMeshes.RadialSegments}_v{GeometryVersion}" :
                shape.Type == PrimitiveColliderFitter.Kind.Frustum ? $"Frustum_S{SixPrimitiveMeshes.RadialSegments}_v{GeometryVersion}_{digest}" :
                $"TrapezoidPrism_v{GeometryVersion}_" + digest;
            string path = folder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null || File.Exists(path))
            {
                if (existing is Mesh saved && saved.vertices.SequenceEqual(vertices) && saved.triangles.SequenceEqual(triangles))
                    return saved;
                throw new InvalidDataException("标准碰撞Mesh已被修改或路径被其它资产占用，请检查：" + path);
            }
            EnsureFolder(folder);
            var mesh=new Mesh{name=name,vertices=vertices,triangles=triangles};
            mesh.RecalculateBounds();mesh.RecalculateNormals();
            AssetDatabase.CreateAsset(mesh,path);AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }
    }
}
