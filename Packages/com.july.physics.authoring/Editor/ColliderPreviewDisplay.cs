using System.Collections.Generic;
using UnityEngine;

namespace July.Physics.Authoring
{
    internal static class ColliderPreviewDisplay
    {
        internal static Mesh RoundMesh(float radius, float height)
        {
            const int segments = 24, rings = 16;
            float extent = Mathf.Max(0, height * .5f - radius);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int y = 0; y <= rings; y++)
            {
                // Duplicate the equator once to join the two hemispheres with a cylinder.
                float angle = -Mathf.PI * .5f + Mathf.PI * y / rings;
                float sy = Mathf.Sin(angle), r = Mathf.Cos(angle) * radius;
                for (int x = 0; x <= segments; x++)
                {
                    float a = 2 * Mathf.PI * x / segments;
                    vertices.Add(new Vector3(Mathf.Cos(a) * r, sy * radius + (y < rings / 2 ? -extent : extent), Mathf.Sin(a) * r));
                }

                if (y == rings / 2 - 1 && extent > 0)
                {
                    for (int x = 0; x <= segments; x++)
                    {
                        float a = 2 * Mathf.PI * x / segments;
                        vertices.Add(new Vector3(Mathf.Cos(a) * radius, -extent, Mathf.Sin(a) * radius));
                    }
                }
            }

            int rowCount = vertices.Count / (segments + 1);
            for (int y = 0; y < rowCount - 1; y++)
                for (int x = 0; x < segments; x++)
                {
                    int a = y * (segments + 1) + x, b = a + segments + 1;
                    triangles.AddRange(new[]{a, b, a + 1, a + 1, b, b + 1});
                }

            var mesh = new Mesh{name = "Collider preview only", hideFlags = HideFlags.HideAndDontSave};
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static Mesh BoxMesh(Vector3 size)
        {
            var h = size * .5f;
            var mesh = new Mesh{name = "Collider preview only", hideFlags = HideFlags.HideAndDontSave, vertices = new[]{new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z)}, triangles = new[]{0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5}};
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
