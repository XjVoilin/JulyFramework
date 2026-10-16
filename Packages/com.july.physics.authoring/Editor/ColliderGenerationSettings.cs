using System;
using UnityEngine;

namespace July.Physics.Authoring
{
    [Serializable]
    public sealed class ColliderGenerationSettings
    {
        public float Tolerance = 0.4f;
        public int MaxShapes = 6;

        internal void Validate()
        {
            if (float.IsNaN(Tolerance) || Tolerance < 0.05f || Tolerance > 1f)
                throw new ArgumentException("贴合误差必须在 0.05–1 之间。");
            if (MaxShapes < 1 || MaxShapes > 16)
                throw new ArgumentException("整物品最大形状数必须在 1–16 之间。");
        }
    }

    [Serializable]
    public sealed class ColliderMeshSource
    {
        public Mesh Mesh;
        public Transform Transform;

        public ColliderMeshSource(Mesh mesh, Transform transform)
        {
            Mesh = mesh;
            Transform = transform;
        }
    }
}
