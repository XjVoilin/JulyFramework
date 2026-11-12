using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Physics.Authoring
{
    public interface IColliderAuthoringAdapter
    {
        ColliderAuthoringProfile CreateProfile();
        bool CanHandle(GameObject root);
        ColliderAssetLayout Inspect(GameObject root, ColliderAuthoringProfile profile);
        void Prepare(GameObject root, ColliderAuthoringProfile profile, IReadOnlyList<string> targetIds);
        ColliderGenerationRequest CreateRequest(GameObject root, ColliderAuthoringProfile profile, string targetId);
        void Complete(GameObject root, IReadOnlyList<ColliderTargetResult> results);
    }

    public sealed class ColliderAssetLayout
    {
        public Transform ItemRoot, SourceRoot;
        public List<ColliderMeshSource> Sources;
        public ColliderTargetLayout[] Targets;
        public string Error;
        public string Badge, BadgeTooltip;
        public bool BadgeWarning;
    }
    public sealed class ColliderTargetLayout
    {
        public Transform OutputRoot, MissingFrame;
        public Collider[] Shapes = Array.Empty<Collider>();
        public int DisabledCount;
        public string Issue, Notice;
        public bool ExternalShapes;
        public IColliderCandidateProcessor Processor;
    }
    public readonly struct ColliderTargetResult
    {
        public readonly string TargetId;
        public readonly ColliderGenerationResult Result;
        public ColliderTargetResult(string targetId, ColliderGenerationResult result) { TargetId = targetId; Result = result; }
    }

    public sealed class DefaultColliderAuthoringAdapter : IColliderAuthoringAdapter
    {
        public ColliderAuthoringProfile CreateProfile() => new ColliderAuthoringProfile("default", "碰撞体总览",
            new[] { new ColliderOutputTarget("default", "碰撞体", "Colliders", ColliderGeometryGoal.Structure) });
        public bool CanHandle(GameObject root) => root.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null);
        public ColliderAssetLayout Inspect(GameObject root, ColliderAuthoringProfile profile)
        {
            var source = profile.SourcePath.Length == 0 ? root.transform : root.transform.Find(profile.SourcePath);
            var result = new ColliderAssetLayout { ItemRoot = root.transform, SourceRoot = source, Targets = new ColliderTargetLayout[profile.Targets.Length] };
            var outputs = profile.Targets.Select(t => root.transform.Find(t.Path)).ToArray();
            for (int i = 0; i < outputs.Length; i++)
            {
                var shapes = outputs[i] != null ? outputs[i].GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
                result.Targets[i] = new ColliderTargetLayout { OutputRoot = outputs[i], MissingFrame = root.transform,
                    Shapes = shapes.Where(c => c.enabled).ToArray(), DisabledCount = shapes.Count(c => !c.enabled) };
            }
            if (source == null) { result.Error = "模型来源节点不存在。"; return result; }
            result.Sources = ColliderGeometry.Scan(source).Where(s => !outputs.Any(o => o != null && s.Transform.IsChildOf(o))).ToList();
            var unassigned = root.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !outputs.Any(o => o != null && c.transform.IsChildOf(o))).ToArray();
            if (unassigned.Length > 0)
                foreach (var target in result.Targets) target.Notice = $"另有{unassigned.Length}个组外Collider，应用时保留；仅替换所选输出组。";
            return result;
        }
        public void Prepare(GameObject root, ColliderAuthoringProfile profile, IReadOnlyList<string> targetIds)
        {
            foreach (var target in profile.Targets.Where(t => targetIds.Contains(t.Id)))
                ColliderAuthoringGroups.Ensure(root.transform, target.Path);
        }
        public ColliderGenerationRequest CreateRequest(GameObject root, ColliderAuthoringProfile profile, string targetId)
        {
            var target = profile.Targets.Single(t => t.Id == targetId); var output = root.transform.Find(target.Path);
            var request = ColliderGeneration.CreateRequest(output, target.Settings.Copy());
            request.Sources = Inspect(root, profile).Sources; request.MeshAssetDirectory = profile.MeshDirectory;
            return request;
        }
        public void Complete(GameObject root, IReadOnlyList<ColliderTargetResult> results) { }
    }

    public static class ColliderAuthoringAdapters
    {
        static readonly Dictionary<string, Func<IColliderAuthoringAdapter>> factories = new Dictionary<string, Func<IColliderAuthoringAdapter>>();
        public static void Register(string id, Func<IColliderAuthoringAdapter> create)
        {
            if (factories.ContainsKey(id)) throw new InvalidOperationException("碰撞体适配标识重复：" + id);
            factories.Add(id, create);
        }
        internal static IColliderAuthoringAdapter Resolve(GameObject root)
        {
            var matches = factories.Values.Select(f => f()).Where(a => a.CanHandle(root)).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("多个项目适配可处理该Prefab，请使用明确的项目入口。");
            return matches.SingleOrDefault() ?? new DefaultColliderAuthoringAdapter();
        }
    }
    public static class ColliderAuthoringGroups
    {
        public static Transform Ensure(Transform root, string path)
        {
            ColliderAuthoringProfile.ValidateNodePath(path);
            foreach (string part in path.Split('/'))
            {
                var next = root.Find(part);
                if (next == null)
                {
                    var node = new GameObject(part); node.layer = root.gameObject.layer;
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(node, root.gameObject.scene);
                    Undo.RegisterCreatedObjectUndo(node, "创建碰撞体输出组"); node.transform.SetParent(root, false); next = node.transform;
                }
                root = next;
            }
            return root;
        }
    }
}
