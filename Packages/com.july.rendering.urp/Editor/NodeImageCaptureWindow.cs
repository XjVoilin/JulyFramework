using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace July.Rendering.URP.Editor
{
    public sealed class NodeImageCaptureWindow : EditorWindow
    {
        [SerializeField] GameObject _node;
        [SerializeField] Camera _camera;
        [SerializeField] string _outputPath;

        [MenuItem("JulyGF/渲染/节点出图")]
        public static void Open()
        {
            var window = GetWindow<NodeImageCaptureWindow>("节点出图");
            window._node = Selection.activeGameObject;
            window._outputPath = null;
        }

        void OnGUI()
        {
            if (_camera == null)
            {
                var cameras = SceneManager.GetActiveScene().GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
                _camera = cameras.FirstOrDefault(camera => camera.CompareTag("MainCamera")) ?? cameras.FirstOrDefault();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _node = (GameObject)EditorGUILayout.ObjectField("节点", _node, typeof(GameObject), true);
                if (GUILayout.Button("当前选择", GUILayout.Width(70)))
                {
                    _node = Selection.activeGameObject;
                    _outputPath = null;
                }
            }
            _camera = (Camera)EditorGUILayout.ObjectField("取景相机", _camera, typeof(Camera), true);
            var renderers = _node == null ? new Renderer[0] : _node.GetComponentsInChildren<Renderer>()
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff).ToArray();
            bool ready = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset && _camera != null
                && _node != null && _node.scene.IsValid() && renderers.Length > 0;
            if (ready)
            {
                var frame = GetFrame(renderers);
                ready = frame.Valid;
                if (ready)
                {
                    EditorGUILayout.LabelField("相机像素尺寸", $"{_camera.pixelWidth} × {_camera.pixelHeight}");
                    EditorGUILayout.LabelField("裁切输出尺寸", $"{frame.Pixels.x} × {frame.Pixels.y}");
                    EditorGUILayout.LabelField("面片世界宽高", $"{frame.Size.x:F6} × {frame.Size.y:F6}");
                }
                else EditorGUILayout.HelpBox("节点包围盒需要在相机前方，并具有有效投影面积。", MessageType.Warning);
            }
            EditorGUILayout.HelpBox("保留当前相机的位置、角度和投影，按节点的屏幕包围范围裁切透明 PNG。导出后在节点同级生成透明面片并设好尺寸和变换；重复导出更新对应面片，原节点保留。", MessageType.Info);
            if (_node != null && renderers.Length == 0) EditorGUILayout.HelpBox("该节点下没有可见的 Renderer。", MessageType.Warning);

            if (string.IsNullOrEmpty(_outputPath)) _outputPath = "Assets/" + (_node == null ? "Node" : _node.name) + ".png";
            using (new EditorGUILayout.HorizontalScope())
            {
                _outputPath = EditorGUILayout.TextField("保存路径", _outputPath);
                if (GUILayout.Button("选择…", GUILayout.Width(60)))
                {
                    string fullPath = Path.GetFullPath(_outputPath);
                    string selected = EditorUtility.SaveFilePanel("导出节点图片", Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath), "png");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        string projectPath = FileUtil.GetProjectRelativePath(selected.Replace('\\', '/'));
                        _outputPath = string.IsNullOrEmpty(projectPath) ? selected : projectPath;
                    }
                }
            }
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset)
                EditorGUILayout.HelpBox("当前项目需要使用 URP 渲染管线。", MessageType.Warning);
            EditorGUILayout.LabelField("保存位置", "Assets 内；同时保存面片材质和网格");
            using (new EditorGUI.DisabledScope(!ready || string.IsNullOrEmpty(_outputPath)))
            {
                if (GUILayout.Button("导出并生成面片"))
                {
                    string path = Path.GetFullPath(_outputPath);
                    string assetPath = FileUtil.GetProjectRelativePath(path.Replace('\\', '/'));
                    if (!assetPath.StartsWith("Assets/"))
                    {
                        EditorUtility.DisplayDialog("保存路径", "请保存到项目 Assets 目录内，生成的面片需要引用贴图资源。", "确定");
                        return;
                    }
                    Capture(path, renderers);
                }
            }
        }

        (Rect Viewport, Vector2Int Pixels, Vector3 Center, Vector2 Size, bool Valid) GetFrame(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                        var point = _camera.WorldToViewportPoint(corner);
                        if (point.z <= 0) return default;
                        min = Vector2.Min(min, point);
                        max = Vector2.Max(max, point);
                    }
            int left = Mathf.FloorToInt(min.x * _camera.pixelWidth);
            int bottom = Mathf.FloorToInt(min.y * _camera.pixelHeight);
            int right = Mathf.CeilToInt(max.x * _camera.pixelWidth);
            int top = Mathf.CeilToInt(max.y * _camera.pixelHeight);
            var pixels = new Vector2Int(right - left, top - bottom);
            if (pixels.x <= 0 || pixels.y <= 0) return default;
            var viewport = Rect.MinMaxRect((float)left / _camera.pixelWidth, (float)bottom / _camera.pixelHeight,
                (float)right / _camera.pixelWidth, (float)top / _camera.pixelHeight);
            float depth = _camera.WorldToViewportPoint(bounds.center).z;
            var a = _camera.ViewportToWorldPoint(new Vector3(viewport.xMin, viewport.yMin, depth));
            var b = _camera.ViewportToWorldPoint(new Vector3(viewport.xMax, viewport.yMin, depth));
            var c = _camera.ViewportToWorldPoint(new Vector3(viewport.xMin, viewport.yMax, depth));
            var center = _camera.ViewportToWorldPoint(new Vector3(viewport.center.x, viewport.center.y, depth));
            return (viewport, pixels, center, new Vector2(Vector3.Distance(a, b), Vector3.Distance(a, c)), true);
        }

        void Capture(string path, Renderer[] renderers)
        {
            var frame = GetFrame(renderers);
            var size = frame.Pixels;
            var others = FindObjectsOfType<Renderer>(true).Where(renderer => !renderer.transform.IsChildOf(_node.transform)).ToArray();
            var previousVisibility = others.Select(renderer => renderer.forceRenderingOff).ToArray();
            var oldActive = RenderTexture.active;
            bool oldAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            var temporary = new GameObject("Node Image Capture") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture target = null;
            Texture2D image = null;
            Texture2D asset = null;
            string assetPath = FileUtil.GetProjectRelativePath(path.Replace('\\', '/'));
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                foreach (var renderer in others) renderer.forceRenderingOff = true;
                var camera = temporary.AddComponent<Camera>();
                camera.CopyFrom(_camera);
                camera.enabled = false;
                camera.transform.SetPositionAndRotation(_camera.transform.position, _camera.transform.rotation);
                if (_camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                    EditorUtility.CopySerialized(data, temporary.AddComponent<UniversalAdditionalCameraData>());
                var additional = camera.GetUniversalAdditionalCameraData();
                additional.renderType = CameraRenderType.Base;
                additional.renderPostProcessing = false;
                camera.allowHDR = false;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                target = RenderTexture.GetTemporary(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.targetTexture = target;
                // Crop the original clip coordinates; do not reframe or change projection type.
                var crop = Matrix4x4.identity;
                crop.m00 = 1 / frame.Viewport.width;
                crop.m11 = 1 / frame.Viewport.height;
                crop.m03 = (1 - 2 * frame.Viewport.center.x) / frame.Viewport.width;
                crop.m13 = (1 - 2 * frame.Viewport.center.y) / frame.Viewport.height;
                camera.projectionMatrix = crop * _camera.projectionMatrix;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });

                RenderTexture.active = target;
                image = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                asset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }
            finally
            {
                for (int i = 0; i < others.Length; i++) others[i].forceRenderingOff = previousVisibility[i];
                RenderTexture.active = oldActive;
                ShaderUtil.allowAsyncCompilation = oldAsyncCompilation;
                DestroyImmediate(temporary);
                if (image != null) DestroyImmediate(image);
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
            var quad = CreateOrUpdateQuad(assetPath, asset, frame.Center, frame.Size);
            Debug.Log($"节点图片已导出：{size.x} × {size.y}，{path}\n面片世界尺寸：{frame.Size.x:F6} × {frame.Size.y:F6}", asset);
            Debug.Log($"同级面片已生成：{quad.name}，原节点保留。", quad);
            Selection.activeGameObject = quad;
        }

        GameObject CreateOrUpdateQuad(string imagePath, Texture2D image, Vector3 center, Vector2 size)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            const string action = "生成节点图片面片";
            Undo.SetCurrentGroupName(action);
            string prefix = Path.ChangeExtension(imagePath, null);
            string materialPath = prefix + "_Image.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            Undo.RecordObject(material, action);
            material.shader = Shader.Find("Universal Render Pipeline/Unlit");
            material.SetTexture("_BaseMap", image);
            material.SetColor("_BaseColor", Color.white);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0);
            material.shaderKeywords = new[] { "_SURFACE_TYPE_TRANSPARENT" };
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;

            string meshPath = prefix + "_Quad.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = _node.name + "_Image" };
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            var parent = _node.transform.parent;
            var siblings = parent == null ? _node.scene.GetRootGameObjects().Select(item => item.transform) : parent.Cast<Transform>();
            var filter = siblings.Where(item => item.gameObject != _node).Select(item => item.GetComponent<MeshFilter>())
                .FirstOrDefault(item => item != null && item.sharedMesh == mesh);
            GameObject quad;
            if (filter == null)
            {
                quad = new GameObject(GameObjectUtility.GetUniqueNameForSibling(parent, _node.name + "_Image"), typeof(MeshFilter), typeof(MeshRenderer));
                SceneManager.MoveGameObjectToScene(quad, _node.scene);
                Undo.RegisterCreatedObjectUndo(quad, action);
                Undo.SetTransformParent(quad.transform, parent, action);
            }
            else quad = filter.gameObject;
            var renderer = quad.GetComponent<MeshRenderer>();
            Undo.RecordObjects(new UnityEngine.Object[] { quad, quad.transform, quad.GetComponent<MeshFilter>(), renderer, mesh }, action);
            quad.layer = _node.layer;
            quad.transform.SetPositionAndRotation(center, _camera.transform.rotation);
            quad.transform.localScale = Vector3.one;
            quad.transform.SetSiblingIndex(_node.transform.GetSiblingIndex() + 1);
            var right = _camera.transform.right * (size.x * .5f);
            var up = _camera.transform.up * (size.y * .5f);
            // Inverse-transform each world corner to compensate rotated/nonuniform parents.
            mesh.Clear();
            mesh.vertices = new[]
            {
                quad.transform.InverseTransformPoint(center - right - up),
                quad.transform.InverseTransformPoint(center + right - up),
                quad.transform.InverseTransformPoint(center - right + up),
                quad.transform.InverseTransformPoint(center + right + up)
            };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            quad.GetComponent<MeshFilter>().sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            EditorUtility.SetDirty(mesh);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(material);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(_node.scene);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            return quad;
        }
    }
}
