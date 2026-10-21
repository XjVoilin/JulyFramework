using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace July.AssetCheck
{
    // 保持现有枚举值稳定：当前页签和旧版忽略记录持久化了这些值。
    public enum AssetCheckKind
    {
        Texture,
        Model,
        Material,
        PrefabScene,
        Audio,
        Urp,
        Project
    }

    public enum AssetCheckPlatform
    {
        MiniGame
    }

    [Serializable]
    public sealed class TextureStandards
    {
        public TextureImporterFormat compressionFormat = TextureImporterFormat.ASTC_6x6;

        public TextureImporterFormat[] allowedFormats =
            { TextureImporterFormat.ASTC_6x6, TextureImporterFormat.ASTC_8x8 };

        public TextureImporterFormat[] allowedAtlasFormats =
            { TextureImporterFormat.ASTC_6x6, TextureImporterFormat.ASTC_8x8 };

        public int maxTextureSize = 1024;
        public bool allowReadWrite;
        public bool requireUncompressedAtlasSources = true;
        public TextureImporterFormat atlasCompressionFormat = TextureImporterFormat.ASTC_6x6;
        public int maxAtlasSize = 2048;
        public bool allowAtlasReadWrite;
    }

    [Serializable]
    public sealed class AudioStandards
    {
        public int maxSampleRate = 44100;
        public int maxChannels = 2;
    }

    public enum AdditionalLightStandard
    {
        Disabled,
        PerVertex,
        PerPixel
    }

    [Serializable]
    public sealed class UrpStandards
    {
        public bool allowHdr;
        public bool allowDepthTexture;
        public bool allowOpaqueTexture;
        public int maxMsaa = 1;
        public float maxRenderScale = 1f;
        public bool requireSrpBatcher = true;
        public bool allowMainLightShadows = true;
        public int maxMainShadowResolution = 512;
        public float maxShadowDistance = 50f;
        public int maxShadowCascades = 1;
        public bool allowSoftShadows;
        public AdditionalLightStandard additionalLights = AdditionalLightStandard.Disabled;
        public bool allowAdditionalLightShadows;
    }

    [Serializable]
    public sealed class ProjectStandards
    {
        public ColorSpace colorSpace = ColorSpace.Linear;
        public bool automaticGraphicsApi;

        public UnityEngine.Rendering.GraphicsDeviceType[] graphicsApis =
            { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 };
    }

    [Serializable]
    public sealed class ModelStandards
    {
        public bool allowLights;
        public bool allowCameras;
        public bool allowReadWrite;
        public bool allowGeneratedColliders;
        public int maxTrianglesPerMesh = 2000;
    }

    [Serializable]
    public sealed class PlatformStandards
    {
        public AssetCheckPlatform platform;
        public TextureStandards texture = new TextureStandards();
        public ModelStandards model = new ModelStandards();
        public AudioStandards audio = new AudioStandards();
        public UrpStandards urp = new UrpStandards();
        public ProjectStandards project = new ProjectStandards();
    }

    [Serializable]
    public sealed class IgnoredAsset
    {
        public string guid;
        public string projectPath;
        public string lastKnownPath;
        public AssetCheckKind kind;
        public string Id => string.IsNullOrEmpty(projectPath) ? guid : projectPath;
    }

    [Serializable]
    public sealed class AssetCheckSettings
    {
        public int version;
        public AssetCheckPlatform platform;
        public string scope;
        public List<PlatformStandards> standards;
        public List<IgnoredAsset> ignoredAssets;

        public static AssetCheckSettings CreateDefault() => new AssetCheckSettings
        {
            version = 4,
            platform = AssetCheckPlatform.MiniGame,
            scope = "Assets",
            standards = new List<PlatformStandards> { new() },
            ignoredAssets = new List<IgnoredAsset>()
        };

        public PlatformStandards CurrentStandards => standards.Find(value => value.platform == platform);

        public AssetCheckSettings Copy() => JsonUtility.FromJson<AssetCheckSettings>(JsonUtility.ToJson(this));

        internal AssetCheckSettings UpgradeVersionOne()
        {
            if (standards == null || standards.Count != 1 || standards[0] == null ||
                standards[0].platform != AssetCheckPlatform.MiniGame || standards[0].texture == null ||
                standards[0].model == null)
                throw new InvalidDataException("旧版资源检查配置缺少贴图或模型标准。");
            var next = CreateDefault();
            next.platform = platform;
            next.scope = scope;
            next.ignoredAssets = ignoredAssets;
            next.CurrentStandards.model = standards[0].model;
            next.CurrentStandards.texture.compressionFormat = standards[0].texture.compressionFormat;
            next.CurrentStandards.texture.allowedFormats =
                LegacyFormats(next.CurrentStandards.texture.compressionFormat);
            next.Validate();
            return next;
        }

        internal AssetCheckSettings UpgradeVersionTwo()
        {
            if (standards == null || standards.Any(entry => entry == null || entry.texture == null))
                throw new InvalidDataException("旧版配置缺少类型标准。");
            foreach (var entry in standards)
            {
                entry.texture.allowedFormats = LegacyFormats(entry.texture.compressionFormat);
                entry.texture.allowedAtlasFormats = LegacyFormats(entry.texture.atlasCompressionFormat);
            }

            return UpgradeVersionThree();
        }

        internal AssetCheckSettings UpgradeVersionThree()
        {
            if (standards == null || standards.Any(entry => entry == null || entry.texture == null))
                throw new InvalidDataException("旧版配置缺少贴图标准。");
            foreach (var entry in standards)
                entry.texture.requireUncompressedAtlasSources = true;
            version = 4;
            Validate();
            return this;
        }

        private static TextureImporterFormat[] LegacyFormats(TextureImporterFormat target) =>
            target == TextureImporterFormat.ASTC_6x6
                ? new[] { TextureImporterFormat.ASTC_6x6, TextureImporterFormat.ASTC_8x8 }
                : new[] { target };

        // 仅在配置读写边界调用；内部调用方依赖已经建立的校验契约。
        public void Validate()
        {
            if (version != 4)
                throw new InvalidDataException("不支持的资源检查配置版本，请检查 version 字段。");
            if (!Enum.IsDefined(typeof(AssetCheckPlatform), platform))
                throw new InvalidDataException("资源检查配置包含不支持的平台。");
            ValidateScope(scope);
            if (standards == null || standards.Count != 1 || standards[0] == null ||
                standards[0].platform != AssetCheckPlatform.MiniGame ||
                standards[0].texture == null || standards[0].model == null ||
                standards[0].audio == null || standards[0].urp == null || standards[0].project == null)
                throw new InvalidDataException("资源检查配置缺少小游戏的类型标准。");
            ValidateFormats(standards[0].texture.allowedFormats, standards[0].texture.compressionFormat);
            ValidateFormats(standards[0].texture.allowedAtlasFormats, standards[0].texture.atlasCompressionFormat);
            if (standards[0].model.maxTrianglesPerMesh <= 0)
                throw new InvalidDataException("Mesh 三角面数上限必须大于零。");
            var current = standards[0];
            ValidateTextureSize(current.texture.maxTextureSize);
            ValidateTextureSize(current.texture.maxAtlasSize);
            if (current.audio.maxSampleRate <= 0 || current.audio.maxChannels < 1 || current.audio.maxChannels > 8)
                throw new InvalidDataException("音频采样率必须为正数，声道上限必须在 1 到 8 之间。");
            var urp = current.urp;
            if (!new[] { 1, 2, 4, 8 }.Contains(urp.maxMsaa) ||
                !new[] { 256, 512, 1024, 2048, 4096 }.Contains(urp.maxMainShadowResolution) ||
                float.IsNaN(urp.maxRenderScale) || urp.maxRenderScale < 0.1f || urp.maxRenderScale > 2f ||
                float.IsNaN(urp.maxShadowDistance) || float.IsInfinity(urp.maxShadowDistance) ||
                urp.maxShadowDistance < 0 ||
                urp.maxShadowCascades < 1 || urp.maxShadowCascades > 4 ||
                !Enum.IsDefined(typeof(AdditionalLightStandard), urp.additionalLights))
                throw new InvalidDataException("URP 标准包含不支持的采样、分辨率、比例、距离或光源配置。");
            if (!new[] { ColorSpace.Gamma, ColorSpace.Linear }.Contains(current.project.colorSpace) ||
                current.project.graphicsApis == null || current.project.graphicsApis.Length == 0 ||
                current.project.graphicsApis.Distinct().Count() != current.project.graphicsApis.Length ||
                current.project.graphicsApis.Any(api =>
                    api == UnityEngine.Rendering.GraphicsDeviceType.Null ||
                    !Enum.IsDefined(typeof(UnityEngine.Rendering.GraphicsDeviceType), api)))
                throw new InvalidDataException("项目标准包含无效的颜色空间或 Graphics API 列表。");
            if (ignoredAssets == null)
                throw new InvalidDataException("资源检查配置缺少 ignoredAssets 列表。");
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ignored in ignoredAssets)
            {
                if (ignored == null || !Enum.IsDefined(typeof(AssetCheckKind), ignored.kind) ||
                    (string.IsNullOrEmpty(ignored.projectPath)
                        ? ignored.guid == null || !Regex.IsMatch(ignored.guid, "^[a-fA-F0-9]{32}$")
                        : !string.IsNullOrEmpty(ignored.guid) ||
                          !AssetCheckTarget.ProjectFiles.Contains(ignored.projectPath)) ||
                    !guids.Add(ignored.Id))
                    throw new InvalidDataException("忽略名单包含无效或重复的资源记录。");
            }
        }

        private static void ValidateFormats(TextureImporterFormat[] allowed, TextureImporterFormat target)
        {
            if (allowed == null || allowed.Length == 0 || allowed.Distinct().Count() != allowed.Length ||
                allowed.Any(format => !MiniGameProfile.TextureFormats.Contains(format)) || !allowed.Contains(target))
                throw new InvalidDataException("允许格式必须非空、无重复且受支持，修改目标必须属于允许格式。");
        }

        private static void ValidateTextureSize(int value)
        {
            if (value < 32 || value > 16384 || (value & (value - 1)) != 0)
                throw new InvalidDataException("贴图尺寸上限必须为 32 到 16384 之间的 2 的幂。");
        }

        public static void ValidateScope(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains("\\") ||
                path != "Assets" && !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.Split('/').Length != Array.FindAll(path.Split('/'), part =>
                    part.Length > 0 && part != "." && part != "..").Length)
                throw new InvalidDataException("扫描范围必须是 Assets 下的项目文件夹。");
        }
    }

    internal static class MiniGameProfile
    {
        internal static readonly string[] TextureTargets = { "WebGL", "MiniGame", "WeixinMiniGame" };

        internal static readonly List<TextureImporterFormat> TextureFormats = new List<TextureImporterFormat>
        {
            TextureImporterFormat.ASTC_4x4,
            TextureImporterFormat.ASTC_5x5,
            TextureImporterFormat.ASTC_6x6,
            TextureImporterFormat.ASTC_8x8,
            TextureImporterFormat.ASTC_10x10,
            TextureImporterFormat.ASTC_12x12
        };
    }
}