using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace July.Physics.Authoring
{
    [CustomEditor(typeof(ColliderAuthoringTarget))]
    internal sealed class ColliderAuthoringInspector : UnityEditor.Editor
    {
        private ColliderGenerationSettings _settings;
        private ColliderGenerationRequest _request;
        private Transform _sourceRoot;
        private bool _showSources;
        private bool _showShapes = true;
        private bool _advanced;
        private static bool _busy;
        private string _status;
        private string _error;

        private void OnEnable()
        {
            _settings = ColliderAuthoringContext.LastSettings();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.RepaintAll();
        }

        private void OnUndoRedo()
        {
            if (_busy) return;
            _status = null;
            _error = null;
            if (_request != null) RescanSources();
            Repaint();
            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            var output = ((ColliderAuthoringTarget)target).transform;
            using (new EditorGUI.DisabledScope(_busy || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (EditorUtility.IsPersistent(output) ||
                    (PrefabUtility.IsPartOfPrefabInstance(output) && PrefabStageUtility.GetPrefabStage(output.gameObject) == null))
                {
                    EditorGUILayout.HelpBox("请进入 Prefab 模式调整，修改会直接显示在 Scene 中。", MessageType.Info);
                    if (GUILayout.Button("进入 Prefab 模式调整", GUILayout.Height(26)))
                        Selection.activeGameObject = ColliderAuthoringContext.EditableGroup(output).gameObject;
                    return;
                }
                if (_request == null)
                {
                    try
                    {
                        var context = ColliderAuthoringContext.Create(output, _settings);
                        _request = context.Request;
                        _sourceRoot = context.SourceRoot;
                        _error = null;
                    }
                    catch (ArgumentException error) { _error = error.Message; }
                    catch (InvalidOperationException error) { _error = error.Message; }
                }
                if (_request == null)
                {
                    EditorGUILayout.HelpBox(_error, MessageType.Warning);
                    return;
                }
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("输出分组", output, typeof(Transform), true);
                _showSources = EditorGUILayout.Foldout(_showSources, $"来源 Mesh（{_request.Sources.Count}）", true);
                if (_showSources)
                {
                    EditorGUI.BeginChangeCheck();
                    _sourceRoot = (Transform)EditorGUILayout.ObjectField("模型扫描根", _sourceRoot, typeof(Transform), true);
                    var rescan = GUILayout.Button("重新扫描 Mesh");
                    if (EditorGUI.EndChangeCheck() || rescan)
                    {
                        RescanSources();
                        _status = _error == null ? "来源已修改，重新生成后生效。" : null;
                    }
                }
                EditorGUI.BeginChangeCheck();
                _settings.Tolerance = EditorGUILayout.Slider("贴合误差", _settings.Tolerance, 0.05f, 1f);
                _settings.MaxShapes = EditorGUILayout.IntSlider("最大形状数", _settings.MaxShapes, 1, 16);
                if (EditorGUI.EndChangeCheck()) _status = "参数已修改，重新生成后生效。";
                _advanced = EditorGUILayout.Foldout(_advanced, "高级设置", true);
                if (_advanced)
                {
                    EditorGUI.BeginChangeCheck();
                    _request.Material = (PhysicMaterial)EditorGUILayout.ObjectField("物理材质", _request.Material, typeof(PhysicMaterial), false);
                    _request.Enabled = EditorGUILayout.Toggle("启用碰撞体", _request.Enabled);
                    if (EditorGUI.EndChangeCheck()) _status = "参数已修改，重新生成后生效。";
                }
                EditorGUI.BeginChangeCheck();
                _showShapes = EditorGUILayout.Toggle("显示碰撞轮廓", _showShapes);
                if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
                EditorGUILayout.LabelField($"当前分组：{output.GetComponentsInChildren<Collider>(true).Length} 个碰撞体", EditorStyles.miniLabel);
                EditorGUILayout.HelpBox("重新生成会替换此分组内的全部形状和手调结果，可通过 Ctrl+Z 撤销。保存沿用 Unity 的保存设置。", MessageType.Info);
                using (new EditorGUI.DisabledScope(_request.Sources.Count == 0))
                    if (GUILayout.Button(_busy ? "正在生成…" : "重新生成碰撞体", GUILayout.Height(28))) Generate();
                if (_request.Sources.Count == 0) EditorGUILayout.HelpBox("没有可用的来源 Mesh，请检查显示配置。", MessageType.Warning);
                if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
                if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
            }
        }

        private void RescanSources()
        {
            try { _request.Sources = ColliderGeometry.Scan(_sourceRoot); _error = null; }
            catch (ArgumentException error) { _request.Sources.Clear(); _error = error.Message; }
        }

        private async void Generate()
        {
            if (_busy) { _status = "正在生成，请等待完成。"; return; }
            _busy = true;
            _status = null;
            _error = null;
            try
            {
                ColliderAuthoringContext.SaveSettings(_settings);
                var result = await ColliderGeneration.GenerateAsync(_request);
                _status = $"已生成 {result.Colliders.Length} 个形状。" +
                    (result.MeetsTolerance ? "达到贴合目标，请检查轮廓。" : "尚未达到贴合目标，可增加形状数或手动调整。");
            }
            catch (Exception error) { _error = error.Message; Debug.LogException(error); }
            finally
            {
                _busy = false;
                InternalEditorUtility.RepaintAllViews();
            }
        }

        private void OnSceneGUI()
        {
            if (!_showShapes || _request == null || _request.OutputRoot == null) return;
            foreach (var shape in _request.OutputRoot.GetComponentsInChildren<Collider>(true)) DrawShape(shape);
        }

        private static void DrawShape(Collider shape)
        {
            using (new Handles.DrawingScope(new Color(0.2f, 0.85f, 1f), shape.transform.localToWorldMatrix))
            {
                if (shape is BoxCollider box) Handles.DrawWireCube(box.center, box.size);
                else if (shape is SphereCollider sphere) DrawSphere(sphere.center, sphere.radius);
                else if (shape is CapsuleCollider capsule)
                {
                    var axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                    var side = capsule.direction == 0 ? Vector3.up : Vector3.right;
                    var cross = Vector3.Cross(side, axis);
                    var offset = axis * Mathf.Max(0, capsule.height * 0.5f - capsule.radius);
                    var top = capsule.center + offset;
                    var bottom = capsule.center - offset;
                    Handles.DrawWireDisc(top, axis, capsule.radius);
                    Handles.DrawWireDisc(bottom, axis, capsule.radius);
                    foreach (var radial in new[] { side, -side, cross, -cross })
                        Handles.DrawLine(top + radial * capsule.radius, bottom + radial * capsule.radius);
                    Handles.DrawWireArc(top, cross, side, 180, capsule.radius);
                    Handles.DrawWireArc(bottom, cross, side, -180, capsule.radius);
                    Handles.DrawWireArc(top, side, -cross, 180, capsule.radius);
                    Handles.DrawWireArc(bottom, side, -cross, -180, capsule.radius);
                }
            }
        }

        private static void DrawSphere(Vector3 center, float radius)
        {
            Handles.DrawWireDisc(center, Vector3.right, radius);
            Handles.DrawWireDisc(center, Vector3.up, radius);
            Handles.DrawWireDisc(center, Vector3.forward, radius);
        }
    }
}
