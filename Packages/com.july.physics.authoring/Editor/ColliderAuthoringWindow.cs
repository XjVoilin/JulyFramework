using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace July.Physics.Authoring
{
    public sealed class ColliderAuthoringContext
    {
        public Transform SourceRoot;
        public ColliderGenerationRequest Request;
    }

    public sealed class ColliderAuthoringWindow : EditorWindow
    {
        private const string SettingsKey = "July.Physics.Authoring.LastSettings.Primitives.v2";
        [SerializeField] private Transform _sourceRoot;
        [SerializeField] private ColliderGenerationRequest _request = new ColliderGenerationRequest();
        private bool _busy;
        private bool _advanced;
        private bool _showSources;
        private Vector2 _scroll;
        private string _status;

        /// <summary>Optional project editor customization; the package never references project types.</summary>
        public static event Action<ColliderAuthoringContext> ContextOpening;

        public static ColliderGenerationSettings LastSettings()
        {
            var json = EditorPrefs.GetString(SettingsKey, "");
            return string.IsNullOrEmpty(json) ? new ColliderGenerationSettings() : JsonUtility.FromJson<ColliderGenerationSettings>(json);
        }

        [MenuItem("GameObject/JulyGF/根据 Mesh 生成碰撞体…", false, 49)]
        private static void OpenSelection() => Open(Selection.activeTransform);

        [MenuItem("GameObject/JulyGF/根据 Mesh 生成碰撞体…", true)]
        private static bool CanOpenSelection() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            Selection.transforms.Length == 1;

        /// <summary>The selected Transform is the complete replacement scope, including an empty group.</summary>
        public static void Open(Transform outputRoot)
        {
            if (outputRoot == null) throw new ArgumentException("请选择专用于碰撞体的父节点。");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式后制作碰撞体。");
            var window = GetWindow<ColliderAuthoringWindow>("Collider 制作");
            if (window._busy) throw new InvalidOperationException("当前正在生成，请等待结束后切换对象。");
            outputRoot = EditableGroup(outputRoot);
            ColliderGeneration.ValidateEditableOutput(outputRoot);
            var body = outputRoot.GetComponentInParent<Rigidbody>(true);
            var stage = PrefabStageUtility.GetPrefabStage(outputRoot.gameObject);
            var sourceRoot = body != null ? body.transform : stage != null ? stage.prefabContentsRoot.transform : outputRoot.root;
            var context = new ColliderAuthoringContext
            {
                SourceRoot = sourceRoot,
                Request = GroupDefaults(outputRoot)
            };
            ContextOpening?.Invoke(context);
            context.Request.Sources = ColliderGeometry.Scan(context.SourceRoot);
            window._sourceRoot = context.SourceRoot;
            window._request = context.Request;
            window._status = null;
            window.minSize = new Vector2(510, 450);
            window.Show();
        }

        private static Transform EditableGroup(Transform group)
        {
            if (!EditorUtility.IsPersistent(group)) return group;
            var path = AssetDatabase.GetAssetPath(group);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请先为导入模型创建可编辑的 Prefab。");
            var indices = new Stack<int>();
            for (var node = group; node.parent != null; node = node.parent) indices.Push(node.GetSiblingIndex());
            var stage = PrefabStageUtility.OpenPrefab(path);
            var target = stage.prefabContentsRoot.transform;
            while (indices.Count > 0) target = target.GetChild(indices.Pop());
            return target;
        }

        private static ColliderGenerationRequest GroupDefaults(Transform group)
        {
            var request = new ColliderGenerationRequest
            {
                OutputRoot = group, Settings = LastSettings(), Layer = group.gameObject.layer
            };
            var shapes = group.GetComponentsInChildren<Collider>(true);
            if (shapes.Length == 0) return request;
            var sample = shapes[0];
            foreach (var shape in shapes)
                if (shape.gameObject.layer != sample.gameObject.layer || shape.isTrigger != sample.isTrigger ||
                    shape.enabled != sample.enabled || shape.sharedMaterial != sample.sharedMaterial ||
                    shape.contactOffset != sample.contactOffset)
                    throw new ArgumentException("所选分组包含不同的 Collider 设置，请选择设置一致的专用分组。");
            request.Layer = sample.gameObject.layer;
            request.IsTrigger = sample.isTrigger;
            request.Enabled = sample.enabled;
            request.Material = sample.sharedMaterial;
            request.ContactOffset = sample.contactOffset;
            return request;
        }

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(_busy || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.LabelField("显示模型 → 少量基础碰撞体", EditorStyles.boldLabel);
                _sourceRoot = (Transform)EditorGUILayout.ObjectField("模型扫描根", _sourceRoot, typeof(Transform), true);
                _showSources = EditorGUILayout.Foldout(_showSources, $"来源 Mesh（{_request.Sources.Count}）", true);
                if (_showSources)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("重新扫描 Mesh")) RunUiAction(() => _request.Sources = ColliderGeometry.Scan(_sourceRoot));
                        if (GUILayout.Button("新增来源")) _request.Sources.Add(new ColliderMeshSource(null, null));
                    }
                    for (var i = 0; i < _request.Sources.Count; i++)
                    {
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                _request.Sources[i].Mesh = (Mesh)EditorGUILayout.ObjectField(_request.Sources[i].Mesh, typeof(Mesh), false);
                                if (GUILayout.Button("移除", GUILayout.Width(48))) { _request.Sources.RemoveAt(i); break; }
                            }
                            _request.Sources[i].Transform = (Transform)EditorGUILayout.ObjectField("坐标节点", _request.Sources[i].Transform, typeof(Transform), true);
                        }
                    }
                }
                EditorGUILayout.Space();
                EditorGUI.BeginChangeCheck();
                var output = (Transform)EditorGUILayout.ObjectField("输出父节点", _request.OutputRoot, typeof(Transform), true);
                if (EditorGUI.EndChangeCheck()) RunUiAction(() => Open(output));
                EditorGUILayout.HelpBox("重新生成会替换此分组内的 Collider 和全部子节点，包括手调结果。请选择专用碰撞节点。", MessageType.Info);
                _request.Settings.Tolerance = EditorGUILayout.Slider(new GUIContent("贴合误差", "基于体素估计的多余覆盖比例；越小越贴合，达到数量预算后可能无法满足。"), _request.Settings.Tolerance, 0.05f, 1f);
                _request.Settings.MaxShapes = EditorGUILayout.IntSlider("最大形状数", _request.Settings.MaxShapes, 1, 16);
                _advanced = EditorGUILayout.Foldout(_advanced, "Collider 设置", true);
                if (_advanced)
                {
                    _request.Layer = EditorGUILayout.LayerField("输出图层", _request.Layer);
                    _request.IsTrigger = EditorGUILayout.Toggle("Is Trigger", _request.IsTrigger);
                    _request.Enabled = EditorGUILayout.Toggle("Enabled", _request.Enabled);
                    _request.Material = (PhysicMaterial)EditorGUILayout.ObjectField("物理材质", _request.Material, typeof(PhysicMaterial), false);
                    _request.ContactOffset = EditorGUILayout.FloatField("接触偏移", _request.ContactOffset);
                }
                EditorGUILayout.HelpBox("参数仅记住本机最近使用值，不保存每个 Prefab 的制作配置。", MessageType.None);
                using (new EditorGUI.DisabledScope(_request.OutputRoot == null || _request.Sources.Count == 0))
                    if (GUILayout.Button("生成 / 替换整组", GUILayout.Height(30))) Generate();
                EditorGUILayout.EndScrollView();
            }
            if (_busy) EditorGUILayout.HelpBox("正在拟合基础形状；完成前保留原碰撞体。", MessageType.Info);
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
        }

        private async void Generate()
        {
            _busy = true;
            try
            {
                EditorPrefs.SetString(SettingsKey, JsonUtility.ToJson(_request.Settings));
                var result = await ColliderGeneration.GenerateAsync(_request);
                _status = $"完成：{result.Colliders.Length} 个基础形状。" +
                    (result.MeetsTolerance ? "请检查轮廓并保存 Prefab。" : "当前预算未达到贴合误差，请手调或增加形状数，并保存 Prefab。");
                SceneView.RepaintAll();
            }
            catch (Exception error) { _status = error.Message; Debug.LogException(error); }
            finally { _busy = false; if (this != null) Repaint(); }
        }

        private void RunUiAction(Action action)
        {
            try { action(); _status = null; }
            catch (Exception error) { _status = error.Message; Debug.LogException(error); }
        }
    }
}
