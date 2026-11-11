using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace July.Physics.Authoring
{
    public static partial class ColliderGeneration
    {
        internal static string CurrentNativeSnapshotIssue(IReadOnlyList<Collider> colliders)
        {
            if (colliders.Count == 0) return "当前整体包围没有可复用的碰撞体。";
            foreach (var collider in colliders)
            {
                if (!(collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider))
                    return "整体包围候选只支持原生盒、球和胶囊，当前包含其它类型。";
            }
            return null;
        }

        internal static Preview CaptureCurrentNativePreview(ColliderGenerationRequest request, IReadOnlyList<Collider> colliders)
        {
            Validate(request);
            if (request.Settings.Goal != ColliderGeometryGoal.Envelope)
                throw new ArgumentException("当前碰撞体快照只用于整体包围候选。");
            string issue = CurrentNativeSnapshotIssue(colliders);
            if (issue != null) throw new InvalidOperationException(issue);
            var frame = PhysicalFitMatrix(request.OutputRoot);
            var inverse = frame.inverse; var rotation = Quaternion.Inverse(frame.rotation);
            float frameScale = ((Vector3)frame.GetColumn(0)).magnitude;
            var shapes = colliders.Select(collider =>
            {
                var transform = collider.transform; var lossy = transform.lossyScale;
                var scale = new Vector3(Mathf.Abs(lossy.x),Mathf.Abs(lossy.y),Mathf.Abs(lossy.z));
                var shape = new PrimitiveColliderFitter.Shape { Rotation = rotation * transform.rotation };
                switch (collider)
                {
                    case BoxCollider box:
                        shape.Type = PrimitiveColliderFitter.Kind.Box;
                        shape.Center = inverse.MultiplyPoint3x4(transform.TransformPoint(box.center));
                        shape.Size = Vector3.Scale(box.size,scale)/frameScale;
                        break;
                    case SphereCollider sphere:
                        shape.Type = PrimitiveColliderFitter.Kind.Sphere;
                        shape.Center = inverse.MultiplyPoint3x4(transform.TransformPoint(sphere.center));
                        shape.Radius = sphere.radius*Mathf.Max(scale.x,Mathf.Max(scale.y,scale.z))/frameScale;
                        shape.Rotation = Quaternion.identity;
                        break;
                    case CapsuleCollider capsule:
                        shape.Type = PrimitiveColliderFitter.Kind.Capsule;
                        shape.Center = inverse.MultiplyPoint3x4(transform.TransformPoint(capsule.center));
                        shape.Radius = capsule.radius*Mathf.Max(scale[(capsule.direction+1)%3],scale[(capsule.direction+2)%3])/frameScale;
                        shape.Height = Mathf.Max(capsule.height*scale[capsule.direction]/frameScale,2*shape.Radius);
                        var axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                        shape.Rotation *= Quaternion.FromToRotation(Vector3.up,axis);
                        break;
                }
                return shape;
            }).ToArray();
            var preview = new Preview { ManuallyReused = true, Settings = request.Settings.Copy(), FitMatrix = frame,
                GeometryHash = HashGeometry(ColliderGeometry.Read(request.Sources,inverse)),
                Fit = new PrimitiveColliderFitter.Result { Shapes = shapes, Note = "手动复用当前整体包围碰撞体；形状原样暂存，未重新拟合。" } };
            // Reuse is a direct snapshot, not a fit/export acceptance pass.
            return preview;
        }

        // Native shape dimensions follow the Collider's world scale. Convex Mesh
        // displays the source triangles, NOT a reconstruction of PhysX cooked hulls.
        public static Mesh CreateCurrentPreviewMesh(IReadOnlyList<Collider> colliders, Matrix4x4 worldToDisplay, bool wireframe = true)
        {
            var vertices = new List<Vector3>();
            var lines = new List<int>();
            foreach (var collider in colliders)
            {
                var transform = collider.transform;
                var scale = transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                Mesh mesh = null;
                Matrix4x4 matrix;
                ColliderGeometry.Geometry geometry;
                try
                {
                    switch (collider)
                    {
                        case BoxCollider box:
                            mesh = ColliderPreviewDisplay.BoxMesh(Vector3.Scale(box.size, scale));
                            matrix = worldToDisplay * Matrix4x4.TRS(transform.TransformPoint(box.center), transform.rotation, Vector3.one);
                            break;
                        case SphereCollider sphere:
                            float radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                            mesh = ColliderPreviewDisplay.RoundMesh(radius, radius * 2);
                            matrix = worldToDisplay * Matrix4x4.Translate(transform.TransformPoint(sphere.center));
                            break;
                        case CapsuleCollider capsule:
                            float radial = Mathf.Max(scale[(capsule.direction + 1) % 3], scale[(capsule.direction + 2) % 3]) * capsule.radius;
                            mesh = ColliderPreviewDisplay.RoundMesh(radial, Mathf.Max(capsule.height * scale[capsule.direction], 2 * radial));
                            var axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                            matrix = worldToDisplay * Matrix4x4.TRS(transform.TransformPoint(capsule.center), transform.rotation * Quaternion.FromToRotation(Vector3.up, axis), Vector3.one);
                            break;
                        case MeshCollider source:
                            if (source.sharedMesh == null) continue; // The caller reports this authoring error separately.
                            geometry = ColliderGeometry.Read(new[] { new ColliderMeshSource(source.sharedMesh, source.transform) }, worldToDisplay);
                            Append(geometry.Vertices, geometry.Indices, Matrix4x4.identity);
                            continue;
                        default: throw new NotSupportedException("当前预览不支持 " + collider.GetType().Name);
                    }
                    Append(mesh.vertices, mesh.triangles, matrix);
                }
                finally { if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); }
            }
            var result = new Mesh { name = "Authored collider preview only", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            result.SetVertices(vertices);
            result.SetIndices(lines, wireframe ? MeshTopology.Lines : MeshTopology.Triangles, 0);
            if (!wireframe) result.RecalculateNormals();
            result.RecalculateBounds();
            return result;

            void Append(Vector3[] points, int[] triangles, Matrix4x4 matrix)
            {
                int offset = vertices.Count;
                foreach (var point in points) vertices.Add(matrix.MultiplyPoint3x4(point));
                if (!wireframe)
                {
                    foreach (int index in triangles) lines.Add(offset + index);
                    return;
                }
                for (int i = 0; i < triangles.Length; i += 3)
                    for (int edge = 0; edge < 3; edge++)
                    {
                        lines.Add(offset + triangles[i + edge]);
                        lines.Add(offset + triangles[i + (edge + 1) % 3]);
                    }
            }
        }
    }
}
