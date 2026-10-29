using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace July.Rendering.URP.Editor
{
    public sealed class SelfShadowBakeWindow : EditorWindow
    {
        [SerializeField] MeshRenderer _receiver;
        [SerializeField] GameObject _casters;
        [SerializeField] Camera _camera;
        [SerializeField] Light _light;
        [SerializeField] string _textureProperty = "_BaseColor";
        [SerializeField] string _outputPath;
        [SerializeField] int _resolution = 4096;
        string PathKey => GetType().FullName + "." + Application.dataPath + ".OutputPath";

        [MenuItem("JulyGF/渲染/自阴影烘焙")]
        public static void Open()
        {
            var window = GetWindow<SelfShadowBakeWindow>("自阴影烘焙");
            var selected = Selection.activeGameObject;
            if (selected == null) return;
            window._receiver = selected.GetComponentsInChildren<MeshRenderer>(true)
                .FirstOrDefault(renderer => renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly);
            var proxies = selected.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly).ToArray();
            window._casters = proxies.Length == 1 ? proxies[0].gameObject : selected;
        }

        void OnEnable() => _outputPath = EditorPrefs.GetString(PathKey, _outputPath ?? "");
        void OnDisable() => EditorPrefs.SetString(PathKey, _outputPath ?? "");

        void OnGUI()
        {
            _receiver = (MeshRenderer)EditorGUILayout.ObjectField("接收模型", _receiver, typeof(MeshRenderer), true);
            _casters = (GameObject)EditorGUILayout.ObjectField("自投影节点", _casters, typeof(GameObject), true);
            _camera = (Camera)EditorGUILayout.ObjectField("参考相机", _camera == null ? Camera.main : _camera, typeof(Camera), true);
            _light = (Light)EditorGUILayout.ObjectField("方向光", _light == null ? RenderSettings.sun : _light, typeof(Light), true);
            _textureProperty = EditorGUILayout.TextField("底色贴图属性", _textureProperty);
            _resolution = EditorGUILayout.IntPopup("离线阴影精度", _resolution, new[] { "512", "1024", "2048", "4096" }, new[] { 512, 1024, 2048, 4096 });
            if (string.IsNullOrEmpty(_outputPath)) _outputPath = "Assets/" + (_receiver == null ? "Model" : _receiver.name) + "_SelfShadow.png";
            using (new EditorGUILayout.HorizontalScope())
            {
                _outputPath = EditorGUILayout.TextField("保存路径", _outputPath);
                if (GUILayout.Button("选择…", GUILayout.Width(60)))
                {
                    var full = Path.GetFullPath(_outputPath);
                    var selected = EditorUtility.SaveFilePanel("保存自阴影贴图", Path.GetDirectoryName(full), Path.GetFileName(full), "png");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        var projectPath = FileUtil.GetProjectRelativePath(selected.Replace('\\', '/'));
                        _outputPath = string.IsNullOrEmpty(projectPath) ? selected : projectPath;
                    }
                }
            }
            EditorGUILayout.HelpBox("按原模型UV把自阴影写入底图Alpha，保留底色和原Shader。仅自投影节点中的静态网格参与，包含禁用的替身；相机需覆盖模型。导出后生成专用材质、应用到接收模型并关闭这些源网格的实时投影，支持场景撤销。重烘会替换Alpha，不累计变黑。", MessageType.Info);
            var error = ValidateInput();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            using (new EditorGUI.DisabledScope(error != null))
                if (GUILayout.Button("烘焙并应用")) Bake();
        }

        string ValidateInput()
        {
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset pipeline || !pipeline.supportsMainLightShadows)
                return "需要启用主光阴影的URP管线。";
            if (_receiver == null || _casters == null || _camera == null || _light == null) return "请指定模型、自投影节点、相机和方向光。";
            if (!_receiver.gameObject.scene.IsValid() || !_casters.scene.IsValid()) return "请选择场景中的对象。";
            if (_light.type != LightType.Directional || _light.shadows == LightShadows.None) return "方向光需要开启阴影。";
            var filter = _receiver.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || filter.sharedMesh.subMeshCount != 1 || _receiver.sharedMaterials.Length != 1)
                return "当前支持一个MeshFilter、一个子网格和一个材质的静态接收模型。";
            var material = _receiver.sharedMaterial;
            if (material == null || !material.HasProperty("_BakedSelfShadow")) return "原Shader需接入_BakedSelfShadow及_BAKED_SELF_SHADOW（底色Alpha自阴影契约）。";
            if (!material.GetTexturePropertyNames().Contains(_textureProperty) || material.GetTexture(_textureProperty) is not Texture2D)
                return "请填写有效的Texture2D底色属性；例如_BaseColor或_BaseMap。";
            if (material.GetTextureScale(_textureProperty) != Vector2.one || material.GetTextureOffset(_textureProperty) != Vector2.zero)
                return "底色需要使用直接UV0，贴图Tiling为1、Offset为0。";
            if (!_casters.GetComponentsInChildren<MeshRenderer>(true).Any(renderer => renderer.GetComponent<MeshFilter>() != null)) return "自投影节点下没有静态网格。";
            if (string.IsNullOrEmpty(_outputPath) || !FileUtil.GetProjectRelativePath(Path.GetFullPath(_outputPath).Replace('\\', '/')).StartsWith("Assets/") || Path.GetExtension(_outputPath).ToLowerInvariant() != ".png")
                return "PNG必须保存在项目Assets目录内。";
            return null;
        }

        internal void Bake()
        {
            var error = ValidateInput();
            if (error != null) throw new InvalidOperationException(error);
            var original = _receiver.sharedMaterial;
            var texture = (Texture2D)original.GetTexture(_textureProperty);
            var casters = _casters.GetComponentsInChildren<MeshRenderer>(true).Where(renderer => renderer.GetComponent<MeshFilter>() != null).ToArray();
            var allRenderers = FindObjectsOfType<Renderer>(true);
            var visibility = allRenderers.Select(renderer => renderer.forceRenderingOff).ToArray();
            var lights = FindObjectsOfType<Light>(true);
            var enabledLights = lights.Select(light => light.enabled).ToArray();
            var oldSun = RenderSettings.sun;
            var oldGraphics = GraphicsSettings.renderPipelineAsset;
            var oldQuality = QualitySettings.renderPipeline;
            var oldActive = RenderTexture.active;
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            var temporary = new GameObject("Self Shadow Bake") { hideFlags = HideFlags.HideAndDontSave };
            UniversalRenderPipelineAsset pipeline = null;
            UniversalRendererData rendererData = null;
            SelfShadowBakeRendererFeature feature = null;
            Material bakeMaterial = null;
            RenderTexture minimum = null, maximum = null, target = null;
            Texture2D image = null;
            bool captured = false;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                image = ReadBase(texture);
                var cameraData = _camera.GetUniversalAdditionalCameraData();
                var source = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
                var sourceData = new SerializedObject(source);
                int index = new SerializedObject(cameraData).FindProperty("m_RendererIndex").intValue;
                if (index < 0) index = sourceData.FindProperty("m_DefaultRendererIndex").intValue;
                var sourceRenderer = sourceData.FindProperty("m_RendererDataList").GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
                if (sourceRenderer == null) throw new InvalidOperationException("参考相机需要使用3D Universal Renderer。");
                pipeline = Instantiate(source); pipeline.hideFlags = HideFlags.HideAndDontSave;
                rendererData = Instantiate(sourceRenderer); rendererData.hideFlags = HideFlags.HideAndDontSave;
                rendererData.rendererFeatures.Clear();
                feature = CreateInstance<SelfShadowBakeRendererFeature>(); feature.hideFlags = HideFlags.HideAndDontSave;
                bakeMaterial = new Material(Shader.Find("Hidden/July/Self Shadow UV Bake")) { hideFlags = HideFlags.HideAndDontSave };
                minimum = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                maximum = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                feature.Mesh = _receiver.GetComponent<MeshFilter>().sharedMesh;
                feature.Matrix = _receiver.localToWorldMatrix; feature.Material = bakeMaterial;
                feature.Minimum = minimum; feature.Maximum = maximum;
                feature.Create(); rendererData.rendererFeatures.Add(feature); rendererData.SetDirty();
                var settings = new SerializedObject(pipeline);
                var list = settings.FindProperty("m_RendererDataList"); list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
                settings.FindProperty("m_DefaultRendererIndex").intValue = 0;
                settings.FindProperty("m_MainLightShadowmapResolution").intValue = _resolution;
                settings.ApplyModifiedPropertiesWithoutUndo();
                foreach (var renderer in allRenderers) renderer.forceRenderingOff = true;
                foreach (var light in lights) light.enabled = false;
                var cloneLight = new GameObject("Bake Light") { hideFlags = HideFlags.HideAndDontSave, layer = _light.gameObject.layer };
                cloneLight.transform.SetParent(temporary.transform, false);
                var sun = cloneLight.AddComponent<Light>(); EditorUtility.CopySerialized(_light, sun);
                sun.enabled = true; sun.transform.SetPositionAndRotation(_light.transform.position, _light.transform.rotation);
                if (_light.TryGetComponent<UniversalAdditionalLightData>(out var additional))
                    EditorUtility.CopySerialized(additional, cloneLight.AddComponent<UniversalAdditionalLightData>());
                RenderSettings.sun = sun;
                var parents = new Dictionary<Transform, Transform>();
                foreach (var caster in casters)
                {
                    var holder = CloneTransform(caster.transform, temporary.transform, parents);
                    holder.gameObject.layer = caster.gameObject.layer;
                    holder.gameObject.AddComponent<MeshFilter>().sharedMesh = caster.GetComponent<MeshFilter>().sharedMesh;
                    var renderer = holder.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterials = caster.sharedMaterials;
                    renderer.renderingLayerMask = caster.renderingLayerMask;
                    renderer.shadowCastingMode = caster.shadowCastingMode == ShadowCastingMode.TwoSided ? ShadowCastingMode.TwoSided : ShadowCastingMode.ShadowsOnly;
                }
                var cameraObject = new GameObject("Bake Camera") { hideFlags = HideFlags.HideAndDontSave };
                cameraObject.transform.SetParent(temporary.transform, false);
                var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(_camera); camera.enabled = false;
                camera.transform.SetPositionAndRotation(_camera.transform.position, _camera.transform.rotation);
                var data = camera.GetUniversalAdditionalCameraData(); data.SetRenderer(0); data.renderType = CameraRenderType.Base; data.renderPostProcessing = false;
                target = RenderTexture.GetTemporary(128, 128, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                camera.rect = new Rect(0, 0, 1, 1); camera.aspect = _camera.aspect; camera.projectionMatrix = _camera.projectionMatrix;
                GraphicsSettings.renderPipelineAsset = pipeline; QualitySettings.renderPipeline = pipeline;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                var low = ReadMask(minimum); var high = ReadMask(maximum);
                int covered = 0, conflicts = 0;
                var pixels = image.GetPixels32();
                for (int i = 0; i < low.Length; i++)
                {
                    if (high[i].a > .5f) { covered++; if (high[i].r - low[i].r > .1f) conflicts++; }
                    pixels[i].a = (byte)Mathf.RoundToInt(Mathf.Clamp01(low[i].r) * 255);
                }
                if (covered == 0 || conflicts > covered * .01f) throw new InvalidOperationException("UV为空或重叠区域阴影不一致；请先修正模型UV。未写入资源。");
                image.SetPixels32(pixels); image.Apply(); captured = true;
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset = oldGraphics; QualitySettings.renderPipeline = oldQuality;
                RenderSettings.sun = oldSun;
                for (int i = 0; i < lights.Length; i++) lights[i].enabled = enabledLights[i];
                for (int i = 0; i < allRenderers.Length; i++) allRenderers[i].forceRenderingOff = visibility[i];
                RenderTexture.active = oldActive; ShaderUtil.allowAsyncCompilation = oldAsync;
                DestroyImmediate(temporary);
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (minimum != null) RenderTexture.ReleaseTemporary(minimum);
                if (maximum != null) RenderTexture.ReleaseTemporary(maximum);
                if (bakeMaterial != null) DestroyImmediate(bakeMaterial);
                if (feature != null) DestroyImmediate(feature);
                if (pipeline != null) DestroyImmediate(pipeline);
                if (rendererData != null) DestroyImmediate(rendererData);
                if (!captured && image != null) DestroyImmediate(image);
            }
            try { SaveAndApply(image, original, casters); }
            finally { DestroyImmediate(image); }
        }

        static Transform CloneTransform(Transform source, Transform root, Dictionary<Transform, Transform> clones)
        {
            if (clones.TryGetValue(source, out var clone)) return clone;
            var parent = source.parent == null ? root : CloneTransform(source.parent, root, clones);
            clone = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave }.transform;
            clone.SetParent(parent, false); clone.localPosition = source.localPosition; clone.localRotation = source.localRotation; clone.localScale = source.localScale;
            clones.Add(source, clone); return clone;
        }

        static Texture2D ReadBase(Texture2D source)
        {
            var old = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(source, rt); RenderTexture.active = rt;
                var image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); image.Apply(); return image;
            }
            finally { RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
        }

        static Color[] ReadMask(RenderTexture target)
        {
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            try { image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image.GetPixels(); }
            finally { DestroyImmediate(image); }
        }

        void SaveAndApply(Texture2D image, Material original, MeshRenderer[] casters)
        {
            string full = Path.GetFullPath(_outputPath);
            string path = FileUtil.GetProjectRelativePath(full.Replace('\\', '/'));
            Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllBytes(full, image.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false; importer.sRGBTexture = true;
            importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(image.width, image.height));
            importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            string materialPath = Path.ChangeExtension(path, ".mat");
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); const string action = "应用自阴影烘焙"; Undo.SetCurrentGroupName(action);
            if (material == null) { material = new Material(original) { name = Path.GetFileNameWithoutExtension(materialPath) }; AssetDatabase.CreateAsset(material, materialPath); }
            Undo.RecordObject(material, action); material.shader = original.shader; material.CopyPropertiesFromMaterial(original);
            material.SetTexture(_textureProperty, texture); material.SetFloat("_BakedSelfShadow", 1); material.EnableKeyword("_BAKED_SELF_SHADOW");
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            Undo.RecordObject(_receiver, action); _receiver.sharedMaterial = material; _receiver.receiveShadows = true;
            foreach (var caster in casters)
            {
                if (caster.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                { Undo.RecordObject(caster.gameObject, action); caster.gameObject.SetActive(false); }
                else { Undo.RecordObject(caster, action); caster.shadowCastingMode = ShadowCastingMode.Off; }
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(_receiver);
            foreach (var caster in casters)
            { PrefabUtility.RecordPrefabInstancePropertyModifications(caster); PrefabUtility.RecordPrefabInstancePropertyModifications(caster.gameObject); }
            EditorSceneManager.MarkSceneDirty(_receiver.gameObject.scene); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
            EditorPrefs.SetString(PathKey, _outputPath);
            Debug.Log($"自阴影已烘焙：{image.width} × {image.height}，{path}。已应用专用材质并保留实时接影。", texture);
            EditorGUIUtility.PingObject(texture);
        }
    }
}
