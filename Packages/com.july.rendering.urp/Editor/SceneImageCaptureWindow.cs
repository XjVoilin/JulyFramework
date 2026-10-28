using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace July.Rendering.URP.Editor
{
    public sealed class SceneImageCaptureWindow : EditorWindow
    {
        const int LongEdge = 2048;
        [SerializeField] Camera _camera;
        [SerializeField] MeshRenderer _framingReference;
        [SerializeField] string _outputPath;
        int _sceneHandle = -1;

        [MenuItem("JulyGF/渲染/场景出图")]
        public static void Open() => GetWindow<SceneImageCaptureWindow>("场景出图");

        string OutputPathKey => GetType().FullName + "." + Application.dataPath + ".OutputPath";

        void OnEnable() => _outputPath = EditorPrefs.GetString(OutputPathKey, _outputPath ?? "");

        void OnDisable() => EditorPrefs.SetString(OutputPathKey, _outputPath ?? "");

        void OnGUI()
        {
            var scene = SceneManager.GetActiveScene();
            if (_sceneHandle != scene.handle)
            {
                _sceneHandle = scene.handle;
                var cameras = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
                _camera = cameras.FirstOrDefault(camera => camera.CompareTag("MainCamera")) ?? cameras.FirstOrDefault();
            }

            EditorGUILayout.LabelField("当前场景", scene.name);
            _camera = (Camera)EditorGUILayout.ObjectField("正交相机", _camera, typeof(Camera), true);
            _framingReference = (MeshRenderer)EditorGUILayout.ObjectField("目标 BG", _framingReference, typeof(MeshRenderer), true);
            bool ready = _camera != null && _camera.orthographic && _camera.gameObject.scene == scene
                && _framingReference != null && _framingReference.gameObject.scene == scene;
            if (ready)
            {
                var frame = ReadFrame();
                var size = GetImageSize(frame.U, frame.V);
                EditorGUILayout.LabelField("自动输出尺寸", $"{size.x} × {size.y}（长边 {LongEdge}）");
            }
            EditorGUILayout.HelpBox("以平面 BG 的完整边界和 UV 自动确定取景与比例。项目内同时生成保持原色并接收实时阴影的配套材质，回贴时使用该材质。场景可见物件均参与渲染。", MessageType.Info);

            if (string.IsNullOrEmpty(_outputPath)) _outputPath = "Assets/" + scene.name + ".png";
            using (new EditorGUILayout.HorizontalScope())
            {
                _outputPath = EditorGUILayout.TextField("保存路径", _outputPath);
                if (GUILayout.Button("选择…", GUILayout.Width(60)))
                {
                    string fullPath = Path.GetFullPath(_outputPath);
                    string selected = EditorUtility.SaveFilePanel("导出场景图片",
                        Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath), "png");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        string projectPath = FileUtil.GetProjectRelativePath(selected.Replace('\\', '/'));
                        _outputPath = string.IsNullOrEmpty(projectPath) ? selected : projectPath;
                    }
                }
            }
            using (new EditorGUI.DisabledScope(!ready || string.IsNullOrEmpty(_outputPath)))
                if (GUILayout.Button("导出 PNG")) Capture(Path.GetFullPath(_outputPath));
        }

        // BG is a flat mesh with an affine, non-overlapping UV layout.
        // Normalize its actual UV rectangle, which need not fill the source atlas.
        (Vector3 Origin, Vector3 U, Vector3 V, Vector2 UvMin, Vector2 UvSize) ReadFrame()
        {
            var mesh = _framingReference.GetComponent<MeshFilter>().sharedMesh;
            using var data = MeshUtility.AcquireReadOnlyMeshData(mesh);
            using var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
            using var uv = new NativeArray<Vector2>(mesh.vertexCount, Allocator.Temp);
            using var indices = new NativeArray<int>((int)mesh.GetIndexCount(0), Allocator.Temp);
            data[0].GetVertices(vertices);
            data[0].GetUVs(0, uv);
            data[0].GetIndices(indices, 0);
            var transform = _framingReference.transform;
            int a = indices[0], b = indices[1], c = indices[2];
            var p = transform.TransformPoint(vertices[a]);
            var edge1 = transform.TransformVector(vertices[b] - vertices[a]);
            var edge2 = transform.TransformVector(vertices[c] - vertices[a]);
            var uv1 = uv[b] - uv[a];
            var uv2 = uv[c] - uv[a];
            float determinant = uv1.x * uv2.y - uv1.y * uv2.x;
            var u = (edge1 * uv2.y - edge2 * uv1.y) / determinant;
            var v = (edge2 * uv1.x - edge1 * uv2.x) / determinant;
            var min = uv[0];
            var max = uv[0];
            for (int i = 1; i < uv.Length; i++) { min = Vector2.Min(min, uv[i]); max = Vector2.Max(max, uv[i]); }
            return (p + u * (min.x - uv[a].x) + v * (min.y - uv[a].y),
                u * (max.x - min.x), v * (max.y - min.y), min, max - min);
        }

        Vector2Int GetImageSize(Vector3 u, Vector3 v)
        {
            var direction = _camera.transform.forward;
            float aspect = Vector3.ProjectOnPlane(u, direction).magnitude / Vector3.ProjectOnPlane(v, direction).magnitude;
            return aspect >= 1 ? new Vector2Int(LongEdge, Mathf.Max(1, Mathf.RoundToInt(LongEdge / aspect)))
                : new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(LongEdge * aspect)), LongEdge);
        }

        void Capture(string path)
        {
            var frame = ReadFrame();
            var size = GetImageSize(frame.U, frame.V);
            var oldActive = RenderTexture.active;
            var oldAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            bool oldInvertCulling = GL.invertCulling;
            var temporary = new GameObject("Scene Image Capture") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture target = null;
            Texture2D image = null;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var camera = temporary.AddComponent<Camera>();
                camera.CopyFrom(_camera);
                camera.enabled = false;
                camera.transform.SetPositionAndRotation(_camera.transform.position, _camera.transform.rotation);
                if (_camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                    EditorUtility.CopySerialized(data, temporary.AddComponent<UniversalAdditionalCameraData>());
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = (float)size.x / size.y;

                // Project along the original viewing direction into the BG's UV axes.
                // This also handles rotated or mirrored UVs without modifying the BG.
                var direction = camera.transform.forward;
                var u = Vector3.Cross(frame.V, direction);
                u /= Vector3.Dot(frame.U, u);
                var v = Vector3.Cross(direction, frame.U);
                v /= Vector3.Dot(frame.V, v);
                var worldToClip = camera.projectionMatrix * camera.worldToCameraMatrix;
                worldToClip.SetRow(0, new Vector4(2 * u.x, 2 * u.y, 2 * u.z, -1 - 2 * Vector3.Dot(u, frame.Origin)));
                worldToClip.SetRow(1, new Vector4(2 * v.x, 2 * v.y, 2 * v.z, -1 - 2 * Vector3.Dot(v, frame.Origin)));
                camera.projectionMatrix = worldToClip * camera.cameraToWorldMatrix;
                GL.invertCulling = oldInvertCulling ^ (Vector3.Dot(Vector3.Cross(frame.U, frame.V), direction) < 0);
                target = RenderTexture.GetTemporary(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });

                RenderTexture.active = target;
                image = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
                string assetPath = FileUtil.GetProjectRelativePath(path.Replace('\\', '/'));
                Texture2D exportedAsset = null;
                if (assetPath.StartsWith("Assets/"))
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                    exportedAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    string materialPath = Path.ChangeExtension(assetPath, null) + "_Baked.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        material = new Material(Shader.Find("July/URP/Baked Surface"));
                        AssetDatabase.CreateAsset(material, materialPath);
                    }
                    material.shader = Shader.Find("July/URP/Baked Surface");
                    material.SetColor("_BaseColor", Color.white);
                    material.SetTexture("_BaseMap", exportedAsset);
                    var scale = new Vector2(1 / frame.UvSize.x, 1 / frame.UvSize.y);
                    material.SetTextureScale("_BaseMap", scale);
                    material.SetTextureOffset("_BaseMap", -Vector2.Scale(frame.UvMin, scale));
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                }
                Debug.Log($"场景图片已导出：{size.x} × {size.y}，{path}", exportedAsset);
            }
            finally
            {
                RenderTexture.active = oldActive;
                ShaderUtil.allowAsyncCompilation = oldAsyncCompilation;
                GL.invertCulling = oldInvertCulling;
                DestroyImmediate(temporary);
                if (image != null) DestroyImmediate(image);
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
