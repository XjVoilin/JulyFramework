using System.Linq;
using UnityEngine;

namespace July.Physics.Authoring
{
    internal readonly struct ColliderGeometryCounts
    {
        internal readonly long Vertices, Triangles;
        internal readonly int MissingMeshes;
        internal ColliderGeometryCounts(long vertices, long triangles, int missingMeshes = 0)
        { Vertices = vertices; Triangles = triangles; MissingMeshes = missingMeshes; }
        public static ColliderGeometryCounts operator +(ColliderGeometryCounts a, ColliderGeometryCounts b) =>
            new ColliderGeometryCounts(a.Vertices + b.Vertices, a.Triangles + b.Triangles, a.MissingMeshes + b.MissingMeshes);
        internal string Text => $"顶点 {Vertices:N0} · 三角面 {Triangles:N0}" + MissingNote;
        internal string ColumnText => $"顶点 {Vertices:N0}\n三角面 {Triangles:N0}" + MissingNote;
        string MissingNote => MissingMeshes == 0 ? "" : $"（缺{MissingMeshes}个网格）";

        // Metadata only: works without allocating/readback of vertex/index buffers.
        // A shared mesh is counted once for each source/collider instance.
        internal static ColliderGeometryCounts FromMesh(Mesh mesh)
        {
            if (mesh == null) return new ColliderGeometryCounts(0, 0, 1);
            long triangles = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles) triangles += mesh.GetIndexCount(i) / 3;
            return new ColliderGeometryCounts(mesh.vertexCount, triangles);
        }
        internal static ColliderGeometryCounts Candidate(ColliderGeneration.Preview preview) =>
            preview.Fit.Shapes.Where(s => s.Type != PrimitiveColliderFitter.Kind.Box &&
                s.Type != PrimitiveColliderFitter.Kind.Sphere && s.Type != PrimitiveColliderFitter.Kind.Capsule)
                .Aggregate(default(ColliderGeometryCounts), (total, shape) => total +
                    new ColliderGeometryCounts(shape.ConvexVertices.LongLength, shape.ConvexTriangles.LongLength / 3));
    }
}
