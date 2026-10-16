using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace July.Physics.Authoring
{
    [Serializable]
    public sealed class ColliderGenerationRequest
    {
        public Transform OutputRoot;
        public List<ColliderMeshSource> Sources = new List<ColliderMeshSource>();
        public ColliderGenerationSettings Settings = new ColliderGenerationSettings();
        public int Layer;
        public bool IsTrigger;
        public bool Enabled = true;
        public PhysicMaterial Material;
        public float ContactOffset = 0.01f;
    }

    public sealed class ColliderGenerationResult
    {
        public ColliderGenerationRequest Request { get; internal set; }
        public Collider[] Colliders { get; internal set; }
        public int[] PreviousLayers { get; internal set; }
        public float ExcessVolumeRatio { get; internal set; }
        public bool MeetsTolerance { get; internal set; }
    }

    public static class ColliderGeneration
    {
        /// <summary>Editor integration, within the replacement Undo group. A failure restores the old group.</summary>
        public static event Action<ColliderGenerationResult> Applied;

        public static async Task<ColliderGenerationResult> GenerateAsync(ColliderGenerationRequest request)
        {
            Validate(request);
            var geometry = ColliderGeometry.Read(request.Sources, request.OutputRoot);
            foreach (var source in request.Sources)
                if (source.Transform.IsChildOf(request.OutputRoot))
                    throw new ArgumentException("来源节点位于将被替换的输出分组内。");
            var settings = new ColliderGenerationSettings { MaxShapes = request.Settings.MaxShapes, Tolerance = request.Settings.Tolerance };
            EditorApplication.LockReloadAssemblies();
            try
            {
                var fit = await Task.Run(() => PrimitiveColliderFitter.Fit(geometry, settings));
                ValidateOutput(request.OutputRoot);
                return Apply(request, fit, settings.Tolerance);
            }
            finally { EditorApplication.UnlockReloadAssemblies(); }
        }

        private static void Validate(ColliderGenerationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateOutput(request.OutputRoot);
            request.Settings.Validate();
            if (request.Layer < 0 || request.Layer > 31 || request.ContactOffset <= 0 ||
                float.IsNaN(request.ContactOffset) || float.IsInfinity(request.ContactOffset))
                throw new ArgumentException("Collider 图层或接触偏移无效。");
        }

        private static void ValidateOutput(Transform output)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式后制作碰撞体。");
            ValidateEditableOutput(output);
            if (output.GetComponentInChildren<Renderer>(true) != null ||
                output.GetComponentInChildren<Rigidbody>(true) != null)
                throw new ArgumentException("输出分组必须专用于形状，不能包含显示 Renderer 或 Rigidbody。");
            // Rotated primitives under a nonuniform/sheared parent cannot represent the fitted geometry faithfully.
            var matrix = output.localToWorldMatrix;
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            var scale = x.magnitude;
            if (scale < 1e-6f || Mathf.Abs(y.magnitude - scale) > scale * 0.001f ||
                Mathf.Abs(z.magnitude - scale) > scale * 0.001f ||
                Mathf.Abs(Vector3.Dot(x, y)) > scale * scale * 0.001f ||
                Mathf.Abs(Vector3.Dot(x, z)) > scale * scale * 0.001f ||
                Mathf.Abs(Vector3.Dot(y, z)) > scale * scale * 0.001f)
                throw new ArgumentException("输出分组及祖先须使用统一缩放且无剪切；显示来源节点可保留非均匀缩放。");
        }

        private static ColliderGenerationResult Apply(ColliderGenerationRequest request, PrimitiveColliderFitter.Result fit, float tolerance)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("拟合基础 Collider 分组");
            try
            {
                var previousLayers = request.OutputRoot.GetComponentsInChildren<Collider>(true)
                    .Select(collider => collider.gameObject.layer).Distinct().ToArray();
                foreach (var collider in request.OutputRoot.GetComponents<Collider>()) Undo.DestroyObjectImmediate(collider);
                for (var i = request.OutputRoot.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(request.OutputRoot.GetChild(i).gameObject);
                var result = new ColliderGenerationResult { Request = request, Colliders = new Collider[fit.Shapes.Length],
                    PreviousLayers = previousLayers, ExcessVolumeRatio = fit.ExcessVolumeRatio, MeetsTolerance = fit.ExcessVolumeRatio <= tolerance };
                for (var i = 0; i < fit.Shapes.Length; i++)
                {
                    var shape = fit.Shapes[i];
                    var child = new GameObject($"JulyShape_{shape.Type}_{i:D3}");
                    SceneManager.MoveGameObjectToScene(child, request.OutputRoot.gameObject.scene);
                    Undo.RegisterCreatedObjectUndo(child, "创建基础形状");
                    child.transform.SetParent(request.OutputRoot, false);
                    child.transform.localPosition = shape.Center;
                    child.transform.localRotation = shape.Rotation;
                    child.layer = request.Layer;
                    Collider collider;
                    switch (shape.Type)
                    {
                        case PrimitiveColliderFitter.Kind.Box:
                            var box = Undo.AddComponent<BoxCollider>(child); box.size = shape.Size; collider = box; break;
                        case PrimitiveColliderFitter.Kind.Sphere:
                            var sphere = Undo.AddComponent<SphereCollider>(child); sphere.radius = shape.Radius; collider = sphere; break;
                        case PrimitiveColliderFitter.Kind.Capsule:
                            var capsule = Undo.AddComponent<CapsuleCollider>(child); capsule.direction = 1;
                            capsule.radius = shape.Radius; capsule.height = shape.Height; collider = capsule; break;
                        default: throw new InvalidOperationException("未知的基础形状。");
                    }
                    collider.sharedMaterial = request.Material;
                    collider.isTrigger = request.IsTrigger;
                    collider.enabled = request.Enabled;
                    collider.contactOffset = request.ContactOffset;
                    result.Colliders[i] = collider;
                }
                Applied?.Invoke(result);
                EditorUtility.SetDirty(request.OutputRoot.gameObject);
                EditorSceneManager.MarkSceneDirty(request.OutputRoot.gameObject.scene);
                Undo.CollapseUndoOperations(undoGroup);
                return result;
            }
            catch { Undo.RevertAllDownToGroup(undoGroup); throw; }
        }

        internal static void ValidateEditableOutput(Transform output)
        {
            if (output == null || EditorUtility.IsPersistent(output))
                throw new ArgumentException("请在 Prefab 模式或场景中指定可编辑的输出分组。");
            if (PrefabUtility.IsPartOfPrefabInstance(output) && PrefabStageUtility.GetPrefabStage(output.gameObject) == null)
                throw new InvalidOperationException("请进入 Prefab 模式编辑碰撞体分组。");
        }
    }
}
