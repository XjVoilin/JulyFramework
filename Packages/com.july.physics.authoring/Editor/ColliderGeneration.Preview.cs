using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace July.Physics.Authoring
{
    public static partial class ColliderGeneration
    {
        // File-based editors can unload their temporary objects after fitting, then
        // bind the result to a fresh editable copy. The caller owns file revision checks.
        public static Preview RetargetPreview(Preview preview, ColliderGenerationRequest request)
        {
            Validate(request);
            var frame = PhysicalFitMatrix(request.OutputRoot);
            if (!frame.Equals(preview.FitMatrix) ||
                !preview.MatchesSettings(request.Settings))
                throw new InvalidOperationException("预览坐标或参数已改变，请重新生成。");
            var geometry = ColliderGeometry.Read(request.Sources, frame.inverse);
            if (HashGeometry(geometry) != preview.GeometryHash)
                throw new InvalidOperationException("预览来源已改变，请重新生成。");
            return new Preview
            {
                Request = request, Fit = preview.Fit, GeometryHash = preview.GeometryHash, Settings = preview.ManuallyReused ? request.Settings.Copy() : preview.Settings,
                ManuallyReused = preview.ManuallyReused,
                FitMatrix = frame, OutputMatrix = request.OutputRoot.localToWorldMatrix,
                Stamp = Stamp(request.OutputRoot)
            };
        }

        // A transient display mesh, in the requested coordinate frame. No Collider
        // or saved mesh asset is created. The caller must destroy the returned mesh.
        public static Mesh CreatePreviewMesh(Preview preview, Matrix4x4 worldToDisplay, bool wireframe = false)
        {
            var parts = preview.Fit.Shapes.Select(shape =>
            {
                Mesh mesh;
                switch (shape.Type)
                {
                    case PrimitiveColliderFitter.Kind.Box:
                        mesh = ColliderPreviewDisplay.BoxMesh(shape.Size);
                        break;
                    case PrimitiveColliderFitter.Kind.Sphere:
                        mesh = ColliderPreviewDisplay.RoundMesh(shape.Radius, shape.Radius * 2);
                        break;
                    case PrimitiveColliderFitter.Kind.Capsule:
                        mesh = ColliderPreviewDisplay.RoundMesh(shape.Radius, shape.Height);
                        break;
                    case PrimitiveColliderFitter.Kind.Convex:
                    case PrimitiveColliderFitter.Kind.Cylinder:
                    case PrimitiveColliderFitter.Kind.Frustum:
                    case PrimitiveColliderFitter.Kind.Prism:
                        mesh = new Mesh { vertices = shape.ConvexVertices, triangles = shape.ConvexTriangles };
                        mesh.RecalculateNormals();
                        break;
                    default: throw new InvalidOperationException("未知预览形状。");
                }
                return new CombineInstance
                {
                    mesh = mesh,
                    transform = worldToDisplay * preview.FitMatrix * Matrix4x4.TRS(shape.Center, shape.Rotation, Vector3.one)
                };
            }).ToArray();
            var result = new Mesh { name = "Collider preview only", hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32 };
            try
            {
                result.CombineMeshes(parts);
                if (wireframe)
                {
                    var triangles = result.triangles;
                    var lines = new int[triangles.Length * 2];
                    for (int i = 0; i < triangles.Length; i += 3)
                        for (int edge = 0; edge < 3; edge++)
                        {
                            lines[i * 2 + edge * 2] = triangles[i + edge];
                            lines[i * 2 + edge * 2 + 1] = triangles[i + (edge + 1) % 3];
                        }
                    result.SetIndices(lines, MeshTopology.Lines, 0);
                }
                return result;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(result);
                throw;
            }
            finally
            {
                foreach (var part in parts) UnityEngine.Object.DestroyImmediate(part.mesh);
            }
        }
    }
}
