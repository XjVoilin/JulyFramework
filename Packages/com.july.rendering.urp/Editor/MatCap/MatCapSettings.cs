using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace July.Rendering.URP.Editor
{
    [Serializable]
    internal sealed class MatCapLightSnapshot
    {
        public string nodeName = "";
        public string sceneGuid = "";
        public string nodeId = "";
        public Quaternion rotation = Quaternion.Euler(36, -38, 0);
        public Color color = Color.white;
        public float intensity = .38f;
        public LightShadows shadows = LightShadows.None;
        public float shadowStrength = 1;
        public float shadowBias = .05f;
        public float shadowNormalBias = .4f;

        internal static MatCapLightSnapshot From(Light light) => new MatCapLightSnapshot
        {
            nodeName = light.name,
            sceneGuid = AssetDatabase.AssetPathToGUID(light.gameObject.scene.path),
            nodeId = GlobalObjectId.GetGlobalObjectIdSlow(light).ToString(),
            rotation = light.transform.rotation,
            color = light.color,
            intensity = light.intensity,
            shadows = light.shadows,
            shadowStrength = light.shadowStrength,
            shadowBias = light.shadowBias,
            shadowNormalBias = light.shadowNormalBias
        };

        internal void Apply(Light light)
        {
            light.type = LightType.Directional;
            light.transform.rotation = rotation;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows;
            light.shadowStrength = shadowStrength;
            light.shadowBias = shadowBias;
            light.shadowNormalBias = shadowNormalBias;
        }
    }

    [Serializable]
    internal sealed class MatCapSettings
    {
        const string DefaultSettingsGuid = "a96b2365f9ad4cebabbd189b2112fc55"; // Defaults/settings.json
        static readonly string FilePath = Path.Combine(MatCapAuthoringFiles.DirectoryPath, "settings.json");
        static MatCapSettings instance;
        internal static MatCapSettings Instance => instance ??= LoadProject(FilePath);

        [SerializeField] internal MatCapLightSnapshot light = new MatCapLightSnapshot();

        internal static MatCapSettings LoadProject(string path)
        {
            if (File.Exists(path)) return JsonUtility.FromJson<MatCapSettings>(File.ReadAllText(path));

            var template = MatCapPresetCatalog.LoadAsset<TextAsset>(DefaultSettingsGuid);
            if (template == null) throw new InvalidOperationException($"找不到 July MatCap 预览光照模板 settings.json（GUID {DefaultSettingsGuid}）。");
            var settings = JsonUtility.FromJson<MatCapSettings>(template.text);
            MatCapAuthoringFiles.Write(path, settings, replace: false);
            return settings;
        }

        internal void Sync(Light sceneLight)
        {
            if (sceneLight == null || sceneLight.type != LightType.Directional ||
                EditorUtility.IsPersistent(sceneLight) || string.IsNullOrEmpty(sceneLight.gameObject.scene.path))
                throw new InvalidOperationException("请在已保存的场景中选中一个方向光节点。");
            var snapshot = MatCapLightSnapshot.From(sceneLight);
            MatCapAuthoringFiles.Write(FilePath, new MatCapSettings { light = snapshot }, replace: true);
            light = snapshot;
        }

    }
}
