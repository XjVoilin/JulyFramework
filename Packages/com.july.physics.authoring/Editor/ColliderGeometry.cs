using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace July.Physics.Authoring
{
    public static class ColliderGeometry
    {
        public static List<ColliderMeshSource> Scan(Transform root)
        {
            if (root == null) throw new ArgumentException("请指定模型扫描根。");
            var result = new List<ColliderMeshSource>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && filter.GetComponent<MeshRenderer>() != null)
                    result.Add(new ColliderMeshSource(filter.sharedMesh, filter.transform));
            return result;
        }

        public static Bounds CalculateBounds(IReadOnlyList<ColliderMeshSource> sources, Transform target)
        {
            var geometry = Read(sources, target);
            var bounds = new Bounds(geometry.Vertices[0], Vector3.zero);
            foreach (var point in geometry.Vertices) bounds.Encapsulate(point);
            return bounds;
        }

        internal sealed class Geometry
        {
            public Vector3[] Vertices;
            public int[] Indices;
        }

        internal static Geometry Read(IReadOnlyList<ColliderMeshSource> sources, Transform target)
        {
            if (target == null || sources == null || sources.Count == 0)
                throw new ArgumentException("生成需要输出节点和至少一个 Mesh 来源。");
            if (Mathf.Abs(target.localToWorldMatrix.determinant) < 1e-10f)
                throw new ArgumentException("输出节点的变换不可逆，请检查零缩放。");
            // Voxel intersection uses positions, not adjacency or triangle winding.
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            foreach (var source in sources)
            {
                if (source == null || source.Mesh == null || source.Transform == null)
                    throw new ArgumentException("Mesh 来源中存在未配置的网格或坐标节点。");
                var matrix = target.worldToLocalMatrix * source.Transform.localToWorldMatrix;
                using var data = MeshUtility.AcquireReadOnlyMeshData(source.Mesh);
                using var positions = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
                data[0].GetVertices(positions);
                var vertexOffset = vertices.Count;
                for (var index = 0; index < positions.Length; index++)
                {
                    var point = matrix.MultiplyPoint3x4(positions[index]);
                    if (float.IsNaN(point.x) || float.IsInfinity(point.x) || float.IsNaN(point.y) ||
                        float.IsInfinity(point.y) || float.IsNaN(point.z) || float.IsInfinity(point.z))
                        throw new ArgumentException($"网格 {source.Mesh.name} 含非有限坐标。");
                    vertices.Add(point);
                }
                for (var sub = 0; sub < data[0].subMeshCount; sub++)
                {
                    var descriptor = data[0].GetSubMesh(sub);
                    if (descriptor.topology != MeshTopology.Triangles)
                        throw new ArgumentException($"网格 {source.Mesh.name} 的子网格 {sub} 不是三角形。");
                    using var triangles = new NativeArray<int>(descriptor.indexCount, Allocator.Temp);
                    data[0].GetIndices(triangles, sub);
                    for (var i = 0; i < triangles.Length; i += 3)
                    {
                        var a = vertexOffset + triangles[i];
                        var b = vertexOffset + triangles[i + 1];
                        var c = vertexOffset + triangles[i + 2];
                        if (vertices[a].Equals(vertices[b]) || vertices[b].Equals(vertices[c]) || vertices[c].Equals(vertices[a])) continue;
                        indices.Add(a); indices.Add(b); indices.Add(c);
                    }
                }
            }
            if (vertices.Count < 3 || indices.Count < 3)
                throw new ArgumentException("来源几何没有有效三角形。");
            // One combined geometry: the budget applies to the item, never once per mesh or material.
            return new Geometry { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
        }
    }
}
