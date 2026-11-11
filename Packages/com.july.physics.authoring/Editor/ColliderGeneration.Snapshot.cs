using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace July.Physics.Authoring
{
    public static partial class ColliderGeneration
    {
        [Serializable] private sealed class Snapshot
        {
            public int version;
            public ColliderGenerationSettings settings;
            public PrimitiveColliderFitter.Shape[] shapes;
            public Matrix4x4 fitMatrix;
            public string geometryHash, summary;
            public double seconds;
            public bool manuallyReused;
        }

        // Detached, pure-data previews can be rendered and retargeted. Scene objects,
        // instance stamps and source vertex buffers do not belong in persistent caches.
        public static string SerializePreview(Preview preview) => JsonUtility.ToJson(new Snapshot
        {
            version = 2, settings = preview.Settings, shapes = preview.Fit.Shapes, fitMatrix = preview.FitMatrix,
            geometryHash = preview.GeometryHash, summary = preview.Summary, seconds = preview.Fit.GenerationSeconds, manuallyReused = preview.ManuallyReused
        });

        internal static Preview CopyForReuse(Preview source, ColliderGenerationSettings settings)
        {
            // The source is already owned preview data. JSON here only deep-copies
            // its shape buffers; no cache/input or geometry acceptance runs.
            var data = JsonUtility.FromJson<Snapshot>(SerializePreview(source));
            return new Preview { Settings = settings.Copy(), ManuallyReused = true,
                FitMatrix = data.fitMatrix, GeometryHash = data.geometryHash,
                Fit = new PrimitiveColliderFitter.Result { Shapes = data.shapes } };
        }

        private static string HashSnapshot(Preview preview)
        {
            var json = JsonUtility.ToJson(new Snapshot
            {
                version = 2, settings = preview.Settings, shapes = preview.Fit.Shapes,
                fitMatrix = preview.FitMatrix, geometryHash = preview.GeometryHash, manuallyReused = preview.ManuallyReused
            });
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json))).Replace("-", "");
        }

        public static Preview DeserializePreview(string json)
        {
            Snapshot data;
            try { data = JsonUtility.FromJson<Snapshot>(json); }
            catch (ArgumentException error) { throw new InvalidDataException("碰撞体预览缓存无法解析。", error); }
            if (data == null || data.version != 2 || data.settings == null || data.shapes == null ||
                data.shapes.Length == 0 || (data.shapes.Length > 128 && !data.manuallyReused) || !Enum.IsDefined(typeof(ColliderGeometryGoal), data.settings.Goal) || data.geometryHash == null ||
                data.geometryHash.Length != 64 || !data.geometryHash.All(Uri.IsHexDigit) ||
                !Finite(data.fitMatrix) || Mathf.Abs(data.fitMatrix.determinant) < 1e-10f ||
                !double.IsFinite(data.seconds) || data.seconds < 0)
                throw new InvalidDataException("预览缓存方案版本或内容无效，请重新生成预览。");
            try { data.settings.Validate(); }
            catch (ArgumentException error) { throw new InvalidDataException("碰撞体预览缓存参数无效。", error); }
            foreach (var shape in data.shapes)
            {
                if (shape == null || !Enum.IsDefined(typeof(PrimitiveColliderFitter.Kind), shape.Type) ||
                    !Finite(shape.Center) || !Finite(shape.Size) || !float.IsFinite(shape.Radius) ||
                    !float.IsFinite(shape.Height) || !float.IsFinite(shape.Bottom) || !float.IsFinite(shape.Top) || !float.IsFinite(shape.Volume) || shape.Volume < 0 ||
                    !Finite(new Vector3(shape.Rotation.x, shape.Rotation.y, shape.Rotation.z)) ||
                    !float.IsFinite(shape.Rotation.w) || Mathf.Abs(Quaternion.Dot(shape.Rotation, shape.Rotation) - 1) > .001f)
                    throw new InvalidDataException("碰撞体预览缓存形状无效。");
                switch (shape.Type)
                {
                    case PrimitiveColliderFitter.Kind.Box:
                        if (Mathf.Min(shape.Size.x, Mathf.Min(shape.Size.y, shape.Size.z)) <= 0)
                            throw new InvalidDataException("缓存盒体尺寸无效。");
                        break;
                    case PrimitiveColliderFitter.Kind.Sphere:
                    case PrimitiveColliderFitter.Kind.Capsule:
                        if (shape.Radius <= 0 || (shape.Type == PrimitiveColliderFitter.Kind.Capsule && shape.Height < 2 * shape.Radius - 1e-6f))
                            throw new InvalidDataException("缓存球体／胶囊尺寸无效。");
                        break;
                    case PrimitiveColliderFitter.Kind.Convex:
                        ValidatePickingMesh(shape);
                        break;
                    case PrimitiveColliderFitter.Kind.Cylinder:
                    case PrimitiveColliderFitter.Kind.Frustum:
                    case PrimitiveColliderFitter.Kind.Prism:
                        if ((shape.Type != PrimitiveColliderFitter.Kind.Prism && shape.Height <= 0) ||
                            (shape.Type == PrimitiveColliderFitter.Kind.Cylinder && shape.Radius <= 0) ||
                            (shape.Type != PrimitiveColliderFitter.Kind.Cylinder && (shape.Bottom < 0 || shape.Top < 0 || Mathf.Max(shape.Bottom, shape.Top) <= 0)) ||
                            (shape.Type == PrimitiveColliderFitter.Kind.Prism && (shape.Size.x <= 0 || shape.Size.y <= 0)))
                            throw new InvalidDataException("缓存规则图元参数无效。");
                        if (shape.ConvexVertices == null || shape.ConvexVertices.Length < 4 ||
                            shape.ConvexVertices.Length > 64 || shape.ConvexVertices.Any(v => !Finite(v)) ||
                            shape.ConvexTriangles == null || shape.ConvexTriangles.Length < 12 || shape.ConvexTriangles.Length % 3 != 0 ||
                            shape.ConvexTriangles.Any(i => i < 0 || i >= shape.ConvexVertices.Length))
                            throw new InvalidDataException("缓存凸网格无效。");
                        break;
                }
            }
            return new Preview
            {
                Settings = data.settings, FitMatrix = data.fitMatrix, GeometryHash = data.geometryHash, ManuallyReused = data.manuallyReused,
                Fit = new PrimitiveColliderFitter.Result { Shapes = data.shapes, Note = data.summary, GenerationSeconds = data.seconds }
            };
        }

        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        private static bool Finite(Matrix4x4 m) => Enumerable.Range(0, 16).All(i => float.IsFinite(m[i]));
        private static string HashGeometry(ColliderGeometry.Geometry geometry)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(geometry.Vertices.Length);
                foreach (var point in geometry.Vertices) { writer.Write(point.x); writer.Write(point.y); writer.Write(point.z); }
                writer.Write(geometry.Indices.Length);
                foreach (int index in geometry.Indices) writer.Write(index);
            }
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(stream.GetBuffer(), 0, (int)stream.Length)).Replace("-", "");
        }
    }
}
