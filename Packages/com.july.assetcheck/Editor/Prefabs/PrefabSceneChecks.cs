using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace July.AssetCheck
{
    internal sealed class MissingScriptRule : IAssetCheckRule<PrefabSceneCheckContext>
    {
        public void Check(PrefabSceneCheckContext context)
        {
            var gameObject = context.GameObject;
            var path = context.AssetPath;
            var location = context.Location;
            var inherited = context.Inherited;
            // 继承的失效组件归源 Prefab 所有；新增组件覆盖
            // 归当前扫描文件所有，包括脚本已被删除的组件覆盖。
            var components = gameObject.GetComponents<Component>();
            using (var serialized = new SerializedObject(gameObject))
            {
                var entries = serialized.FindProperty("m_Component");
                for (var index = 0; index < components.Length; index++)
                {
                    if (components[index] != null || (inherited && !entries.GetArrayElementAtIndex(index).prefabOverride))
                        continue;
                    var field = location + " / Component[" + index + "]";
                    context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.PrefabScene,
                        "hierarchy.missing-script", "Missing Script", "脚本无法解析", "有效脚本", false,
                        resourceName: path + " / " + field, location: field)
                        { ObjectLocation = AssetCheckObjectLocation.Capture(gameObject) });
                }
            }
        }
    }

    internal sealed class HierarchyReferenceRule : IAssetCheckRule<PrefabSceneCheckContext>
    {
        public void Check(PrefabSceneCheckContext context)
        {
            var gameObject = context.GameObject;
            var path = context.AssetPath;
            var location = context.Location;
            var inherited = context.Inherited;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(gameObject) && PrefabUtility.IsPrefabAssetMissing(gameObject))
                context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.PrefabScene,
                    "reference.missing", "失效引用", "嵌套／场景 Prefab 源资源丢失", "有效 Prefab 引用", false,
                    resourceName: path + " / " + location, location: location + " / PrefabSource")
                { ObjectLocation = AssetCheckObjectLocation.Capture(gameObject) });

            var components = gameObject.GetComponents<Component>();
            for (var index = 0; index < components.Length; index++)
            {
                var component = components[index];
                if (component == null)
                    continue;
                var own = !inherited || PrefabUtility.IsAddedComponentOverride(component);
                SerializedReferenceChecks.Check(component, context,
                    location + " / " + component.GetType().Name + "[" + index + "]",
                    property => own || property.prefabOverride);
            }
        }
    }

    internal sealed class PrefabSceneCheckModule : IAssetCheckModule, IIncrementalAssetCheckModule
    {
        private readonly IAssetCheckRule<PrefabSceneCheckContext>[] _rules = AssetCheckRules<PrefabSceneCheckContext>.Create();
        public AssetCheckKind Kind => AssetCheckKind.PrefabScene;
        public string RulesFingerprint => AssetCheckRules<PrefabSceneCheckContext>.Fingerprint;
        public string[] Collect(string scope) => AssetDatabase.FindAssets("t:Prefab", new[] { scope })
            .Where(id => Path.GetExtension(AssetCheckTarget.Resolve(id)).Equals(".prefab", StringComparison.OrdinalIgnoreCase))
            .Concat(AssetDatabase.FindAssets("t:Scene", new[] { scope })).Distinct().ToArray();

        public List<AssetCheckIssue> Check(string assetPath, PlatformStandards standards)
        {
            var issues = new List<AssetCheckIssue>();
            foreach (var step in CheckIncrementally(assetPath, standards, issues)) { }
            return issues;
        }

        public IEnumerable<bool> CheckIncrementally(string assetPath, PlatformStandards standards, List<AssetCheckIssue> issues)
        {
            if (Path.GetExtension(assetPath).Equals(".unity", StringComparison.OrdinalIgnoreCase))
            {
                var existing = SceneManager.GetSceneByPath(assetPath);
                var owned = !existing.IsValid() || !existing.isLoaded;
                var active = SceneManager.GetActiveScene();
                var selection = Selection.objects;
                var scene = owned ? EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Additive) : existing;
                // 在让出执行权给用户操作前，立即还原打开场景引起的修改。
                if (owned)
                {
                    SceneManager.SetActiveScene(active);
                    Selection.objects = selection;
                }
                try
                {
                    foreach (var step in CheckRoots(scene.GetRootGameObjects(), assetPath, scene, issues))
                        yield return step;
                }
                finally
                {
                    // 操作暂停期间，用户可能关闭场景。
                    if (owned && scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, !existing.IsValid());
                }
            }
            else
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.assetPath == assetPath && stage.scene.isDirty)
                {
                    issues.Add(Unverified(assetPath, "Prefab 模式有未保存修改，请保存后检查"));
                    yield break;
                }
                var root = PrefabUtility.LoadPrefabContents(assetPath);
                try
                {
                    foreach (var step in CheckRoots(new[] { root }, assetPath, root.scene, issues))
                        yield return step;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        private IEnumerable<bool> CheckRoots(GameObject[] roots, string path, Scene scene, List<AssetCheckIssue> issues)
        {
            var pending = new Stack<Transform>(roots.Reverse().Select(root => root.transform));
            var batch = 0;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (pending.Count > 0)
            {
                if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
                {
                    issues.Clear();
                    issues.Add(Unverified(path, "场景已关闭或有未保存修改，请保存后检查"));
                    yield break;
                }
                var transform = pending.Pop();
                var sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject);
                var inherited = !string.IsNullOrEmpty(sourcePath) && sourcePath != path &&
                    !PrefabUtility.IsAddedGameObjectOverride(transform.gameObject);
                AssetCheckRules<PrefabSceneCheckContext>.Check(_rules,
                    new PrefabSceneCheckContext(transform.gameObject, path, inherited, issues));
                for (var child = transform.childCount - 1; child >= 0; child--)
                    pending.Push(transform.GetChild(child));
                if (++batch >= 64 || elapsed.ElapsedMilliseconds >= 8)
                {
                    batch = 0;
                    elapsed.Restart();
                    yield return true;
                }
            }
            if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
            {
                issues.Clear();
                issues.Add(Unverified(path, "检查期间资源发生修改，请保存后检查"));
            }
        }

        private AssetCheckIssue Unverified(string path, string reason) => new AssetCheckIssue(path,
            AssetCheckTarget.Id(path), Kind, "hierarchy.unsaved", "未能验证", reason, "已保存的资源", false, true);

        public void Fix(string assetPath, IReadOnlyList<AssetCheckIssue> issues, PlatformStandards standards) =>
            throw new InvalidOperationException("脚本和引用需要人工恢复，检查工具不会删除组件或清空引用。");
    }
}
