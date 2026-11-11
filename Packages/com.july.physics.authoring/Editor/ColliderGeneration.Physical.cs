using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    public static partial class ColliderGeneration
    {
        internal static bool UniformPhysicalFrame(Matrix4x4 matrix)
        {
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            float scale = x.magnitude;
            return scale > 1e-6f && Mathf.Abs(y.magnitude - scale) <= scale * .001f && Mathf.Abs(z.magnitude - scale) <= scale * .001f && Mathf.Abs(Vector3.Dot(x, y)) <= scale * scale * .001f && Mathf.Abs(Vector3.Dot(x, z)) <= scale * scale * .001f && Mathf.Abs(Vector3.Dot(y, z)) <= scale * scale * .001f;
        }

        // Both geometry goals need an undistorted native-Collider frame. Preserve
        // authored group transforms and compensate their full hierarchy on apply.
        internal static Matrix4x4 PhysicalFitMatrix(Transform output) => UniformPhysicalFrame(output.localToWorldMatrix) ? output.localToWorldMatrix : Matrix4x4.TRS(output.position, output.rotation, Vector3.one);
        private static Transform CreatePhysicalOutputFrame(Transform output)
        {
            if (UniformPhysicalFrame(output.localToWorldMatrix))
                return output;
            var fitMatrix = PhysicalFitMatrix(output);
            var parent = output;
            Transform Node(string name, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                var go = new GameObject(name);
                go.layer = output.gameObject.layer;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, output.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(go, "补偿物理输出缩放");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = position;
                go.transform.localRotation = rotation;
                go.transform.localScale = scale;
                parent = go.transform;
                return parent;
            }

            // M = T0 R0 S0 ... Tn Rn Sn. Cancel it exactly with nested
            // inverse scale/pose nodes; decomposing M^-1 into one TRS loses shear.
            for (var node = output; node != null; node = node.parent)
            {
                var scale = node.localScale;
                Node("ScaleCompensation", Vector3.zero, Quaternion.identity, new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z));
                var inverse = Quaternion.Inverse(node.localRotation);
                Node("PoseCompensation", -(inverse * node.localPosition), inverse, Vector3.one);
            }

            return Node("PhysicalFitFrame", fitMatrix.GetColumn(3), output.rotation, Vector3.one);
        }
    }
}
