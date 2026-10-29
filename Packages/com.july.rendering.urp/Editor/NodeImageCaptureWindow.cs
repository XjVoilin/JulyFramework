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
        [SerializeField] bool _includeShadows = true;
        [SerializeField] Renderer _ground;

        [MenuItem("JulyGF/渲染/节点出图")]
        public static void Open()
        {
            var window = GetWindow<NodeImageCaptureWindow>("节点出图");
            window._node = Selection.activeGameObject;
        }

        string OutputPathKey => GetType().FullName + "." + Application.dataPath + ".OutputPath";

        void OnEnable() => _outputPath = EditorPrefs.GetString(OutputPathKey, _outputPath ?? "");

        void OnDisable() => EditorPrefs.SetString(OutputPathKey, _outputPath ?? "");

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
                }
            }
            _camera = (Camera)EditorGUILayout.ObjectField("取景相机", _camera, typeof(Camera), true);
            _includeShadows = EditorGUILayout.Toggle("保留地板实际阴影", _includeShadows);
            if (_includeShadows) _ground = (Renderer)EditorGUILayout.ObjectField("接影地板", _ground, typeof(Renderer), true);
            var renderers = _node == null ? new Renderer[0] : _node.GetComponentsInChildren<Renderer>()
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff).ToArray();
            bool ready = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset && _camera != null
                && _node != null && _node.scene.IsValid() && renderers.Length > 0;
            if (ready && _includeShadows)
            {
                ready = _ground != null && _ground.enabled && _ground.gameObject.activeInHierarchy && !_ground.forceRenderingOff
                    && _ground.gameObject.scene == _node.scene && !_ground.transform.IsChildOf(_node.transform)
                    && _ground.bounds.size.y < .001f && _ground.sharedMaterials.All(material => material != null && material.renderQueue <= 2500);
                if (!ready) EditorGUILayout.HelpBox("请指定节点外的水平、不透明原地板，用它的真实材质提取阴影。", MessageType.Warning);
            }
            if (ready)
            {
                var frame = GetFrame(renderers);
                ready = frame.Valid;
                if (ready)
                {
                    EditorGUILayout.LabelField("相机像素尺寸", $"{_camera.pixelWidth} × {_camera.pixelHeight}");
                    EditorGUILayout.LabelField("地面贴图尺寸", $"{frame.Pixels.x} × {frame.Pixels.y}");
                    EditorGUILayout.LabelField("面片世界宽长", $"{frame.Size.x:F6} × {frame.Size.y:F6}");
                }
                else EditorGUILayout.HelpBox("相机需朝下观察 XZ 平面；节点需位于相机前方，透视相机需高于整个节点。", MessageType.Warning);
            }
            EditorGUILayout.HelpBox("沿取景相机视线将节点投影到 XZ 平面，旋转和透视烘入透明 PNG。宽长按地面投影计算，同级生成并更新面片，原节点保留。开启阴影时使用原地板高度，并提取其有/无节点投影的实际颜色差异；不手调阴影强度。关闭阴影时高度取节点世界 Y。", MessageType.Info);
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

        (Rect Ground, Vector2Int Pixels, Vector3 Center, Vector2 Size, bool Valid) GetFrame(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var eye = _camera.transform.position;
            var direction = _camera.transform.forward;
            float height = _includeShadows && _ground != null ? _ground.bounds.center.y : _node.transform.position.y;
            if (direction.y >= -.0001f || (!_camera.orthographic && eye.y <= Mathf.Max(height, bounds.max.y))) return default;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                        if (_camera.WorldToViewportPoint(corner).z <= 0) return default;
                        var point = _camera.orthographic
                            ? corner + direction * ((height - corner.y) / direction.y)
                            : eye + (corner - eye) * ((height - eye.y) / (corner.y - eye.y));
                        min = Vector2.Min(min, new Vector2(point.x, point.z));
                        max = Vector2.Max(max, new Vector2(point.x, point.z));
                    }
            var light = GetShadowLight();
            if (light != null)
            {
                var lightDirection = light.transform.forward;
                foreach (var renderer in renderers.Where(item => item.shadowCastingMode != ShadowCastingMode.Off
                    && (light.cullingMask & (1 << item.gameObject.layer)) != 0))
                {
                    var caster = renderer.bounds;
                    if (caster.max.y < height) continue;
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y += 2)
                            for (int z = -1; z <= 1; z += 2)
                            {
                                var corner = caster.center + Vector3.Scale(caster.extents, new Vector3(x, y, z));
                                corner.y = Mathf.Max(corner.y, height);
                                var shadow = corner + lightDirection * ((height - corner.y) / lightDirection.y);
                                min = Vector2.Min(min, new Vector2(shadow.x, shadow.z));
                                max = Vector2.Max(max, new Vector2(shadow.x, shadow.z));
                            }
                }
                // Leave room for soft shadow filtering at the projected bounds.
                float padding = (max - min).magnitude * .02f;
                min -= Vector2.one * padding;
                max += Vector2.one * padding;
            }
            var size = max - min;
            if (size.x <= 0 || size.y <= 0) return default;
            var center = new Vector3((min.x + max.x) * .5f, height, (min.y + max.y) * .5f);
            // Keep a uniform texel density in X/Z, based on the source camera's pixel density.
            float densityX = Vector3.Distance(_camera.WorldToScreenPoint(center - Vector3.right * size.x * .5f),
                _camera.WorldToScreenPoint(center + Vector3.right * size.x * .5f)) / size.x;
            float densityZ = Vector3.Distance(_camera.WorldToScreenPoint(center - Vector3.forward * size.y * .5f),
                _camera.WorldToScreenPoint(center + Vector3.forward * size.y * .5f)) / size.y;
            float density = Mathf.Max(densityX, densityZ);
            var pixels = new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(size.x * density)), Mathf.Max(1, Mathf.CeilToInt(size.y * density)));
            return (Rect.MinMaxRect(min.x, min.y, max.x, max.y), pixels, center, size, true);
        }

        Light GetShadowLight()
        {
            if (!_includeShadows || GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset pipeline
                || !pipeline.supportsMainLightShadows || pipeline.mainLightRenderingMode != LightRenderingMode.PerPixel) return null;
            var lights = FindObjectsOfType<Light>().Where(light => light.isActiveAndEnabled && light.type == LightType.Directional
                && light.intensity > 0 && (_camera.cullingMask & (1 << light.gameObject.layer)) != 0).ToArray();
            var main = lights.Contains(RenderSettings.sun) ? RenderSettings.sun : lights.OrderByDescending(light => light.intensity).FirstOrDefault();
            return main != null && main.shadows != LightShadows.None && main.shadowStrength > 0 && main.transform.forward.y < -.0001f ? main : null;
        }

        Color[] RenderPixels(Camera camera, RenderTexture target, Texture2D image)
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            return image.GetPixels();
        }

        // Find an RGBA overlay that reproduces the real floor's shadowed color.
        // Work in the same color space as runtime alpha blending.
        void ApplyGroundShadow(Color[] node, Color[] shadowed, Color[] baseline)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int i = 0; i < node.Length; i++)
            {
                var before = linear ? baseline[i].linear : baseline[i];
                var after = linear ? shadowed[i].linear : shadowed[i];
                float opacity = Mathf.Max(ChannelOpacity(before.r, after.r),
                    Mathf.Max(ChannelOpacity(before.g, after.g), ChannelOpacity(before.b, after.b)));
                var shadow = after - before * (1 - opacity);
                var foreground = linear ? node[i].linear : node[i];
                float alpha = foreground.a + opacity * (1 - foreground.a);
                if (alpha == 0) { node[i] = Color.clear; continue; }
                var color = (foreground + shadow * (1 - foreground.a)) / alpha;
                if (linear) color = color.gamma;
                color.a = alpha;
                node[i] = color;
            }
        }

        static float ChannelOpacity(float before, float after) => after < before
            ? (before - after) / before : after > before ? (after - before) / (1 - before) : 0;

        void Capture(string path, Renderer[] renderers)
        {
            var frame = GetFrame(renderers);
            var size = frame.Pixels;
            var others = FindObjectsOfType<Renderer>(true).Where(renderer => !renderer.transform.IsChildOf(_node.transform)).ToArray();
            var previousVisibility = others.Select(renderer => renderer.forceRenderingOff).ToArray();
            var oldActive = RenderTexture.active;
            bool oldAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            var temporary = new GameObject("Node Image Capture") { hideFlags = HideFlags.HideAndDontSave };
            var sourceModes = renderers.Select(renderer => renderer.shadowCastingMode).ToArray();
            var sourceVisibility = renderers.Select(renderer => renderer.forceRenderingOff).ToArray();
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
                if (_camera.orthographic)
                {
                    var direction = _camera.transform.forward;
                    var u = new Vector3(1, -direction.x / direction.y, 0) / frame.Size.x;
                    var v = new Vector3(0, -direction.z / direction.y, 1) / frame.Size.y;
                    var origin = new Vector3(frame.Ground.xMin, frame.Center.y, frame.Ground.yMin);
                    var worldToClip = _camera.projectionMatrix * _camera.worldToCameraMatrix;
                    worldToClip.SetRow(0, new Vector4(2 * u.x, 2 * u.y, 2 * u.z, -1 - 2 * Vector3.Dot(u, origin)));
                    worldToClip.SetRow(1, new Vector4(2 * v.x, 2 * v.y, 2 * v.z, -1 - 2 * Vector3.Dot(v, origin)));
                    camera.projectionMatrix = worldToClip * camera.cameraToWorldMatrix;
                }
                else
                {
                    // Same eye, looking straight down: the floor rectangle is the image plane.
                    var bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                    var eye = _camera.transform.position;
                    float near = (eye.y - Mathf.Max(bounds.max.y, frame.Center.y)) * .5f;
                    // URP limits shadow distance by farClipPlane; preserve the source fade distance.
                    float far = Mathf.Max(_camera.farClipPlane, (eye.y - Mathf.Min(bounds.min.y, frame.Center.y)) * 1.5f);
                    float scale = near / (eye.y - frame.Center.y);
                    camera.transform.rotation = Quaternion.Euler(90, 0, 0);
                    camera.nearClipPlane = near;
                    camera.farClipPlane = far;
                    camera.projectionMatrix = Matrix4x4.Frustum((frame.Ground.xMin - eye.x) * scale,
                        (frame.Ground.xMax - eye.x) * scale, (frame.Ground.yMin - eye.z) * scale,
                        (frame.Ground.yMax - eye.z) * scale, near, far);
                }
                image = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                var foreground = RenderPixels(camera, target, image);
                if (_includeShadows)
                {
                    _ground.forceRenderingOff = false;
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        renderers[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                        renderers[i].forceRenderingOff = sourceModes[i] == ShadowCastingMode.Off;
                    }
                    var shadowed = RenderPixels(camera, target, image);
                    foreach (var renderer in renderers) renderer.forceRenderingOff = true;
                    var baseline = RenderPixels(camera, target, image);
                    ApplyGroundShadow(foreground, shadowed, baseline);
                    image.SetPixels(foreground);
                    image.Apply();
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                asset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].shadowCastingMode = sourceModes[i];
                    renderers[i].forceRenderingOff = sourceVisibility[i];
                }
                for (int i = 0; i < others.Length; i++) others[i].forceRenderingOff = previousVisibility[i];
                RenderTexture.active = oldActive;
                ShaderUtil.allowAsyncCompilation = oldAsyncCompilation;
                DestroyImmediate(temporary);
                if (image != null) DestroyImmediate(image);
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
            // Keep the overlay above the original floor to avoid coplanar depth rejection.
            var center = frame.Center + (_includeShadows ? Vector3.up * .001f : Vector3.zero);
            var quad = CreateOrUpdateQuad(assetPath, asset, center, frame.Size);
            Debug.Log($"节点图片已导出：{size.x} × {size.y}，{path}\n面片世界宽长：{frame.Size.x:F6} × {frame.Size.y:F6}", asset);
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
                material = new Material(Shader.Find("July/URP/Baked Transparent Surface"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            Undo.RecordObject(material, action);
            material.shader = Shader.Find("July/URP/Baked Transparent Surface");
            material.SetTexture("_BaseMap", image);
            material.SetColor("_BaseColor", Color.white);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetFloat("_ReceiveShadows", 1);
            material.shaderKeywords = new string[0];
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
            quad.transform.SetPositionAndRotation(center, Quaternion.identity);
            quad.transform.localScale = Vector3.one;
            quad.transform.SetSiblingIndex(_node.transform.GetSiblingIndex() + 1);
            var right = Vector3.right * (size.x * .5f);
            var up = Vector3.forward * (size.y * .5f);
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
            renderer.receiveShadows = true;
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
