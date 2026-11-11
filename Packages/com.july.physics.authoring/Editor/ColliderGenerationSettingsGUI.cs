using UnityEditor;

namespace July.Physics.Authoring
{
    public static class ColliderGenerationSettingsGUI
    {
        public static void Draw(ColliderGenerationSettings s)
        {
            var goal = (ColliderGeometryGoal)EditorGUILayout.EnumPopup("几何目标", s.Goal);
            if (goal != s.Goal)
            {
                s.Goal = goal;
                s.TargetCount = ColliderGenerationSettings.ForGoal(goal).TargetCount;
            }
            DrawParameters(s);
        }

        internal static void DrawParameters(ColliderGenerationSettings s)
        {
            if (s.Goal == ColliderGeometryGoal.Structure)
            {
                s.TargetCount = EditorGUILayout.IntSlider("结构贴合碰撞体数上限", s.TargetCount, 1, PickingProxy.GenerationSettings.ConvexCountLimit);
                DrawPickingMeshBudget(s);
            }
            else
            {
                s.TargetCount = EditorGUILayout.IntSlider("整体包围碰撞体数上限", s.TargetCount, 1, 32);
                s.SilhouetteErrorPercent = EditorGUILayout.Slider("允许轮廓误差（%）", s.SilhouetteErrorPercent, 0, 30);
                EditorGUILayout.HelpBox("整体包围只生成原生盒、球和胶囊，不生成MeshCollider。", MessageType.Info);
            }
        }

        internal static void DrawPickingMeshBudget(ColliderGenerationSettings s)
        {
            EditorGUILayout.Space();EditorGUILayout.LabelField("回退凸包",EditorStyles.boldLabel);
            s.MaxHullVertices = EditorGUILayout.IntSlider("单凸包顶点数上限", s.MaxHullVertices, 4, PickingProxy.GenerationSettings.HullVertexLimit);
            s.PickingInwardErrorPercent = EditorGUILayout.Slider("凸包额外内缩（最长边%）", s.PickingInwardErrorPercent, 0, (float)(PickingProxy.GenerationSettings.InwardErrorLimit * 100));
            EditorGUILayout.Space();EditorGUILayout.LabelField("原生碰撞体近似",EditorStyles.boldLabel);
            s.NativeShapeErrorPercent = EditorGUILayout.Slider(new UnityEngine.GUIContent("原生形状偏差（最长边%）", "相对未压缩拟合：原顶点到原生形状的最大内缩，以及原生形状超出凸包支撑平面的最大距离。不是体积百分比，也不是沿射线的深度差。"), s.NativeShapeErrorPercent, 0, 20);
            s.NativeClickChangePercent = EditorGUILayout.Slider(new UnityEngine.GUIContent("原生点击范围变化（%）", "每个采样视角，新增与丢失命中像素之和相对原命中像素的比例。只允许形状偏差距离内的边界变化；数值越大越容易使用原生形状，孔口边缘和主体边缘也可能改变。"), s.NativeClickChangePercent, 0, 30);
            EditorGUILayout.HelpBox("提高原生容差可增加近似机会，也会改变轮廓与孔口边缘。生成后对照检查，再应用。", MessageType.Info);
        }
    }
}
