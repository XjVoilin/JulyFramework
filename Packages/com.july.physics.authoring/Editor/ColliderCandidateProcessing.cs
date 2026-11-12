using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace July.Physics.Authoring
{
    /// <summary>A per-request, immutable project policy. Fingerprint includes its algorithm version and effective inputs.</summary>
    public interface IColliderCandidateProcessor
    {
        string Fingerprint { get; }
        void Process(ColliderCandidateDraft candidate);
    }

    public enum ColliderCandidateShapeKind { Box = 0, Sphere = 1, Capsule = 2, Cylinder = 4, Frustum = 5, Prism = 6, Convex = 7 }

    /// <summary>Detached edits in the fitting frame. Native dimensions and all centers/rotations are editable; mesh geometry is retained.</summary>
    public sealed class ColliderCandidateShape
    {
        readonly PrimitiveColliderFitter.Shape source;
        Vector3 size;
        float radius, height;
        public ColliderCandidateShapeKind Kind => (ColliderCandidateShapeKind)source.Type;
        public Vector3 Center { get; set; }
        public Quaternion Rotation { get; set; }
        public Vector3 BoxSize
        {
            get => size;
            set { Require(Kind == ColliderCandidateShapeKind.Box); size = value; }
        }
        public float Radius
        {
            get => radius;
            set { Require(Kind == ColliderCandidateShapeKind.Sphere || Kind == ColliderCandidateShapeKind.Capsule); radius = value; }
        }
        public float CapsuleHeight
        {
            get => height;
            set { Require(Kind == ColliderCandidateShapeKind.Capsule); height = value; }
        }
        internal ColliderCandidateShape(PrimitiveColliderFitter.Shape shape)
        { source = shape; Center = shape.Center; Rotation = shape.Rotation; size = shape.Size; radius = shape.Radius; height = shape.Height; }
        static void Require(bool supported)
        {
            if (!supported) throw new InvalidOperationException("该候选形状不支持此原生尺寸操作。");
        }
        internal PrimitiveColliderFitter.Shape Seal()
        {
            var shape = new PrimitiveColliderFitter.Shape
            {
                Type = source.Type, Center = Center, Rotation = Rotation, Size = size, Radius = radius, Height = height,
                Bottom = source.Bottom, Top = source.Top, Volume = source.Volume,
                ConvexVertices = source.ConvexVertices, ConvexTriangles = source.ConvexTriangles
            };
            switch (Kind)
            {
                case ColliderCandidateShapeKind.Box:
                    shape.Volume = size.x * size.y * size.z; break;
                case ColliderCandidateShapeKind.Sphere:
                    shape.Volume = 4f / 3f * Mathf.PI * radius * radius * radius; break;
                case ColliderCandidateShapeKind.Capsule:
                    shape.Volume = Mathf.PI * radius * radius * (height - 2 * radius + 4f / 3f * radius); break;
            }
            return shape;
        }
    }

    /// <summary>Only fresh generation invokes processing. The framework seals a separate result before preview/cache/apply.</summary>
    public sealed class ColliderCandidateDraft
    {
        public Bounds SourceBounds { get; }
        public IReadOnlyList<ColliderCandidateShape> Shapes { get; }
        public string Summary { get; set; }
        internal ColliderCandidateDraft(ColliderGeometry.Geometry geometry, PrimitiveColliderFitter.Result fit)
        {
            var bounds = new Bounds(geometry.Vertices[0], Vector3.zero);
            foreach (var point in geometry.Vertices) bounds.Encapsulate(point);
            SourceBounds = bounds;
            Shapes = Array.AsReadOnly(fit.Shapes.Select(shape => new ColliderCandidateShape(shape)).ToArray());
            Summary = fit.Note;
        }
        internal PrimitiveColliderFitter.Shape[] Seal() => Shapes.Select(shape => shape.Seal()).ToArray();
    }
}
