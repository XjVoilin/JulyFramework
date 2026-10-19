using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace July.Physics.Authoring
{
    /// <summary>沿用制作上下文，项目仅配置来源和生成请求。</summary>
    public sealed class ColliderAuthoringContext
    {
        private const string SettingsKey = "July.Physics.Authoring.LastSettings.Primitives.v2";
        public Transform SourceRoot;
        public ColliderGenerationRequest Request;
        public static event Action<ColliderAuthoringContext> Opening;

        public static ColliderGenerationSettings LastSettings()
        {
            var json = EditorPrefs.GetString(SettingsKey, "");
            return string.IsNullOrEmpty(json) ? new ColliderGenerationSettings() : JsonUtility.FromJson<ColliderGenerationSettings>(json);
        }

        internal static void SaveSettings(ColliderGenerationSettings settings) => EditorPrefs.SetString(SettingsKey, JsonUtility.ToJson(settings));

        public static ColliderAuthoringContext Create(Transform outputRoot, ColliderGenerationSettings settings)
        {
            var request = ColliderGeneration.CreateRequest(outputRoot, settings);
            var body = outputRoot.GetComponentInParent<Rigidbody>(true);
            var stage = PrefabStageUtility.GetPrefabStage(outputRoot.gameObject);
            var context = new ColliderAuthoringContext
            {
                SourceRoot = body != null ? body.transform : stage != null ? stage.prefabContentsRoot.transform : outputRoot.root,
                Request = request
            };
            Opening?.Invoke(context);
            context.Request.Sources = ColliderGeometry.Scan(context.SourceRoot);
            return context;
        }

        internal static Transform EditableGroup(Transform group)
        {
            var source = EditorUtility.IsPersistent(group) ? group : PrefabUtility.GetCorrespondingObjectFromSource(group);
            var path = AssetDatabase.GetAssetPath(source);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请先为导入模型创建可编辑的 Prefab。");
            var indices = new Stack<int>();
            for (var node = source; node.parent != null; node = node.parent) indices.Push(node.GetSiblingIndex());
            var stage = PrefabStageUtility.OpenPrefab(path);
            var target = stage.prefabContentsRoot.transform;
            while (indices.Count > 0) target = target.GetChild(indices.Pop());
            return target;
        }
    }
}
