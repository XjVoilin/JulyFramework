using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace July.AssetCheck
{
    public abstract class AssetCheckContext
    {
        public string AssetPath { get; }
        public AssetCheckKind Kind { get; }
        private readonly List<AssetCheckIssue> _issues;
        private object _rule;
        private bool _ruleCanFix;
        internal virtual bool SupportsFix => false;

        internal AssetCheckContext(string path, AssetCheckKind kind, List<AssetCheckIssue> issues)
        { AssetPath = path; Kind = kind; _issues = issues; }

        internal void BindRule<TContext>(IAssetCheckRule<TContext> rule) where TContext : AssetCheckContext
        {
            _rule = rule;
            _ruleCanFix = rule is IAssetCheckFix<TContext>;
        }

        internal void Add(AssetCheckIssue issue)
        {
            if (issue.CanFix && !_ruleCanFix)
                throw new InvalidOperationException("可修改的问题必须由实现修复接口的规则报告：" + _rule.GetType().FullName);
            if (issue.CanFix && !SupportsFix)
                throw new InvalidOperationException("当前检查模块只支持报告问题，不支持自动修改：" + Kind);
            issue.Rule = _rule;
            _issues.Add(issue);
        }

        /// <summary>按规则内的局部编号报告问题，对象位置和文件标识由框架补充。
        /// 检查只读取已保存资源，不修改资源；继承对象的归属由规则判断。</summary>
        public void Report(Object owner, string code, string problem, string actual, string expected,
            string propertyPath = null, bool canFix = false, bool unverified = false)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("问题代码不能为空。", nameof(code));
            var component = owner as Component;
            var gameObject = component != null ? component.gameObject : owner as GameObject;
            var location = gameObject != null ? HierarchyPath(gameObject.transform) : owner != null ? owner.name : "";
            if (component != null)
                location += " / " + component.GetType().Name + "[" + Array.IndexOf(gameObject.GetComponents<Component>(), component) + "]";
            if (propertyPath != null) location += " / " + propertyPath;
            long localId = 0;
            if (owner != null && EditorUtility.IsPersistent(owner))
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(owner, out string _, out localId);
            var type = _rule.GetType();
            Add(new AssetCheckIssue(AssetPath, AssetCheckTarget.Id(AssetPath), Kind,
                type.Assembly.GetName().Name + ":" + type.FullName + "." + code,
                problem, actual, expected, canFix, unverified, resourceName: AssetPath + (location.Length > 0 ? " / " + location : ""),
                localId: localId, location: location, propertyPath: propertyPath)
                { ObjectLocation = AssetCheckObjectLocation.Capture(owner) });
        }

        internal static string HierarchyPath(Transform transform) => transform.parent == null
            ? transform.name + "[" + transform.GetSiblingIndex() + "]"
            : HierarchyPath(transform.parent) + "/" + transform.name + "[" + transform.GetSiblingIndex() + "]";
    }

    public sealed class PrefabSceneCheckContext : AssetCheckContext
    {
        public GameObject GameObject { get; }
        public bool Inherited { get; }
        public bool IsPrefab => AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        internal string Location => HierarchyPath(GameObject.transform);
        internal PrefabSceneCheckContext(GameObject gameObject, string path, bool inherited, List<AssetCheckIssue> issues)
            : base(path, AssetCheckKind.PrefabScene, issues) { GameObject = gameObject; Inherited = inherited; }
    }

    public sealed class TextureCheckContext : AssetCheckContext
    {
        internal override bool SupportsFix => true;
        /// <summary>对于受支持资源，Importer 与 Atlas 中恰有一个有效。</summary>
        public TextureImporter Importer { get; }
        public SpriteAtlas Atlas { get; }
        public bool IsAtlas => Atlas != null;
        public bool IsAtlasSource { get; }
        public TextureStandards Standards { get; }
        internal TextureCheckContext(string path, TextureImporter importer, SpriteAtlas atlas, TextureStandards standards,
            List<AssetCheckIssue> issues, bool isAtlasSource = false) : base(path, AssetCheckKind.Texture, issues)
        { Importer = importer; Atlas = atlas; Standards = standards; IsAtlasSource = isAtlasSource; }
    }

    public sealed class ModelCheckContext : AssetCheckContext
    {
        internal override bool SupportsFix => true;
        public ModelImporter Importer { get; }
        public IReadOnlyList<Mesh> Meshes { get; }
        public ModelStandards Standards { get; }
        internal ModelCheckContext(string path, ModelImporter importer, Mesh[] meshes, ModelStandards standards,
            List<AssetCheckIssue> issues) : base(path, AssetCheckKind.Model, issues)
        { Importer = importer; Meshes = meshes; Standards = standards; }
    }

    public sealed class MaterialCheckContext : AssetCheckContext
    {
        public Material Material { get; }
        internal MaterialCheckContext(Material material, string path, List<AssetCheckIssue> issues)
            : base(path, AssetCheckKind.Material, issues) { Material = material; }
    }

    public sealed class AudioCheckContext : AssetCheckContext
    {
        public AudioClip Clip { get; }
        public AudioStandards Standards { get; }
        internal AudioCheckContext(AudioClip clip, string path, AudioStandards standards, List<AssetCheckIssue> issues)
            : base(path, AssetCheckKind.Audio, issues) { Clip = clip; Standards = standards; }
    }

    public sealed class ProjectCheckContext : AssetCheckContext
    {
        public ProjectStandards Standards { get; }
        internal ProjectCheckContext(string path, ProjectStandards standards, List<AssetCheckIssue> issues)
            : base(path, AssetCheckKind.Project, issues) { Standards = standards; }
    }

    public sealed class UrpCheckContext : AssetCheckContext
    {
        internal override bool SupportsFix => true;
        public SerializedObject Serialized { get; }
        public bool IsPipeline { get; }
        public UrpStandards Standards { get; }
        public UrpFacts Facts { get; }
        internal UrpCheckContext(SerializedObject serialized, string path, bool isPipeline, UrpFacts facts,
            UrpStandards standards, List<AssetCheckIssue> issues) : base(path, AssetCheckKind.Urp, issues)
        { Serialized = serialized; IsPipeline = isPipeline; Facts = facts; Standards = standards; }
    }
}
