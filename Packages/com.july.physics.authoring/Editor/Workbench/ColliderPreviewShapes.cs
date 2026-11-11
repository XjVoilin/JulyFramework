using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace July.Physics.Authoring
{
    // Visible-row display geometry only. Fitted/current collider data is never modified.
    internal sealed class ColliderPreviewShapes : IDisposable
    {
        internal readonly Mesh Surface, Wire;
        readonly Mesh contours;
        readonly Edge[] edges;
        readonly List<int> indices = new List<int>();
        Quaternion? contourPose;

        sealed class Edge
        {
            internal int A, B;
            internal bool Crease;
            internal readonly List<Vector3> Normals = new List<Vector3>();
        }

        internal ColliderPreviewShapes(Mesh surface)
        {
            Surface = surface;
            var vertices = surface.vertices; var triangles = surface.triangles;
            var allLines = new int[triangles.Length * 2];
            for (int i = 0; i < triangles.Length; i += 3)
                for (int k = 0; k < 3; k++)
                { allLines[(i + k) * 2] = triangles[i + k]; allLines[(i + k) * 2 + 1] = triangles[i + (k + 1) % 3]; }
            Wire = new Mesh { name = "Collider full wire preview", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            Wire.vertices = vertices; Wire.SetIndices(allLines, MeshTopology.Lines, 0); Wire.RecalculateBounds();

            var ids = new Dictionary<Vector3Int, int>(); var welded = new List<Vector3>(); var map = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 100000), Mathf.RoundToInt(p.y * 100000), Mathf.RoundToInt(p.z * 100000));
                if (!ids.TryGetValue(key, out int index)) { index = welded.Count; welded.Add(p); ids.Add(key, index); }
                map[i] = index;
            }
            var adjacency = new Dictionary<(int, int), Edge>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var normal = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]);
                if (normal.sqrMagnitude < 1e-16f) continue; // Collapsed pole triangles have no surface normal.
                normal.Normalize();
                for (int k = 0; k < 3; k++)
                {
                    int a = map[triangles[i + k]], b = map[triangles[i + (k + 1) % 3]];
                    if (a == b) continue;
                    var key = (Mathf.Min(a, b), Mathf.Max(a, b));
                    if (!adjacency.TryGetValue(key, out var edge)) adjacency.Add(key, edge = new Edge { A = a, B = b });
                    edge.Normals.Add(normal);
                }
            }
            edges = new Edge[adjacency.Count]; adjacency.Values.CopyTo(edges, 0);
            float cosine = Mathf.Cos(35 * Mathf.Deg2Rad);
            foreach (var edge in edges)
                foreach (var a in edge.Normals)
                    foreach (var b in edge.Normals)
                        if (Vector3.Dot(a, b) < cosine) edge.Crease = true;
            contours = new Mesh { name = "Collider visible contours", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            contours.SetVertices(welded); contours.MarkDynamic();
        }

        internal Mesh Contours(Quaternion rotation)
        {
            if (contourPose == rotation) return contours;
            contourPose = rotation; indices.Clear();
            foreach (var edge in edges)
            {
                bool front = false, back = false;
                foreach (var normal in edge.Normals)
                { if ((rotation * normal).z < 0) front = true; else back = true; }
                if (edge.Crease || edge.Normals.Count == 1 || front && back)
                { indices.Add(edge.A); indices.Add(edge.B); }
            }
            contours.SetIndices(indices, MeshTopology.Lines, 0); contours.RecalculateBounds();
            return contours;
        }

        public void Dispose()
        { Object.DestroyImmediate(Surface); Object.DestroyImmediate(Wire); Object.DestroyImmediate(contours); }
    }
}
