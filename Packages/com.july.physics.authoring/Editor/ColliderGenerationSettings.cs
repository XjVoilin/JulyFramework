using System;
using UnityEngine;
using ProxySettings = July.Physics.Authoring.PickingProxy.GenerationSettings;

namespace July.Physics.Authoring
{
    public enum PickingShapeMode { PreferPrimitives = 0, ConvexOnly = 1 }

    [Serializable]
    public sealed class ColliderGenerationSettings
    {
        public ColliderGeometryGoal Goal = ColliderGeometryGoal.Structure;
        public int TargetCount = ProxySettings.DefaultConvexCount;
        public int MaxHullVertices = ProxySettings.DefaultHullVertices;
        public float PickingInwardErrorPercent = (float)(ProxySettings.DefaultInwardError * 100);
        public PickingShapeMode PickingShapes;
        public float NativeShapeErrorPercent = 10, NativeClickChangePercent = 20;
        public float SilhouetteErrorPercent = 8, DetailAreaPercent = 2, MinimumThicknessPercent = .2f;
        public static ColliderGenerationSettings ForGoal(ColliderGeometryGoal goal) =>
            new ColliderGenerationSettings { Goal = goal, TargetCount = goal == ColliderGeometryGoal.Envelope ? 3 : ProxySettings.DefaultConvexCount };
        internal ColliderGenerationSettings Copy() => (ColliderGenerationSettings)MemberwiseClone();
        internal bool HasCurrentPickingBudget => TargetCount >= 1 && TargetCount <= ProxySettings.ConvexCountLimit &&
            MaxHullVertices >= 4 && MaxHullVertices <= ProxySettings.HullVertexLimit &&
            float.IsFinite(NativeShapeErrorPercent) && NativeShapeErrorPercent >= 0 && NativeShapeErrorPercent <= 20 &&
            float.IsFinite(NativeClickChangePercent) && NativeClickChangePercent >= 0 && NativeClickChangePercent <= 30 &&
            float.IsFinite(PickingInwardErrorPercent) && PickingInwardErrorPercent >= 0 && PickingInwardErrorPercent <= ProxySettings.InwardErrorLimit * 100;
        internal void Validate()
        {
            if (TargetCount < 1 || TargetCount > 128)
                throw new ArgumentException("数量须在1–128。");
            foreach (var v in new[]{SilhouetteErrorPercent, DetailAreaPercent, MinimumThicknessPercent})
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0 || v > 40)
                    throw new ArgumentException("几何容差须在0–40%。");
            if (MinimumThicknessPercent == 0)
                throw new ArgumentException("厚度须大于0。");
            if (Goal == ColliderGeometryGoal.Structure && (MaxHullVertices < 4 || MaxHullVertices > ProxySettings.HullVertexLimit ||
                !Enum.IsDefined(typeof(PickingShapeMode), PickingShapes) ||
                !float.IsFinite(NativeShapeErrorPercent) || NativeShapeErrorPercent < 0 || NativeShapeErrorPercent > 20 ||
                !float.IsFinite(NativeClickChangePercent) || NativeClickChangePercent < 0 || NativeClickChangePercent > 30 ||
                !float.IsFinite(PickingInwardErrorPercent) || PickingInwardErrorPercent < 0 || PickingInwardErrorPercent > ProxySettings.InwardErrorLimit * 100))
                throw new ArgumentException("结构贴合每包顶点须在4–24，凸包内缩与原生形状偏差须在0–20%，点击范围变化须在0–30%。");
        }
    }

    [Serializable]
    public sealed class ColliderMeshSource
    {
        public Mesh Mesh;
        public Transform Transform;
        public ColliderMeshSource(Mesh m, Transform t)
        {
            Mesh = m;
            Transform = t;
        }
    }
}
