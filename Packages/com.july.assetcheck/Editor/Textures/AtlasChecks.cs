using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine.U2D;

namespace July.AssetCheck
{
    internal static class AtlasPlatformSettings
    {
        internal static TextureImporterPlatformSettings Effective(SpriteAtlas atlas, string target)
        {
            var settings = atlas.GetPlatformSettings(target);
            if (!settings.overridden)
            {
                atlas.GetPlatformSettings("DefaultTexturePlatform").CopyTo(settings);
                settings.name = target;
                settings.overridden = false;
            }
            return settings;
        }
    }

    internal sealed class AtlasFormatRule : IAssetCheckFix<TextureCheckContext>
    {
        public string Id => "atlas.compression-format";

        public void Check(TextureCheckContext context)
        {
            if (!context.IsAtlas) return;
            var atlas = context.Atlas;
            var path = context.AssetPath;
            var standards = context.Standards;
            var targets = new List<string>();
            var values = new List<string>();
            var unverified = false;
            var hasProblem = false;
            var unknownTargets = new List<string>();
            foreach (var target in MiniGameProfile.TextureTargets)
            {
                var settings = AtlasPlatformSettings.Effective(atlas, target);
                if (standards.allowedAtlasFormats.Contains(settings.format))
                    continue;
                targets.Add(target);
                unverified |= settings.format == TextureImporterFormat.Automatic;
                if (settings.format == TextureImporterFormat.Automatic) unknownTargets.Add(target);
                hasProblem |= settings.format != TextureImporterFormat.Automatic;
                values.Add(settings.format == TextureImporterFormat.Automatic
                    ? "Automatic（未打包验证）" : TextureFormatRule.FormatName(settings.format));
            }
            if (targets.Count == 0) return;
            context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.Texture,
                Id, "图集压缩格式", string.Join(" / ", values.Distinct()),
                string.Join(" / ", standards.allowedAtlasFormats.Select(TextureFormatRule.FormatName)), true, unverified,
                targets: targets, hasProblem: hasProblem, unverifiedTargets: unknownTargets));
        }

        public void Fix(TextureCheckContext context, AssetCheckIssue issue)
        {
            var atlas = context.Atlas;
            var standards = context.Standards;
            foreach (var target in issue.Targets)
            {
                var settings = AtlasPlatformSettings.Effective(atlas, target);
                settings.overridden = true;
                settings.format = standards.atlasCompressionFormat;
                settings.textureCompression = TextureImporterCompression.Compressed;
                atlas.SetPlatformSettings(settings);
            }
        }
    }

    internal sealed class AtlasSizeRule : IAssetCheckFix<TextureCheckContext>
    {
        public string Id => "atlas.page-size";

        public void Check(TextureCheckContext context)
        {
            if (!context.IsAtlas) return;
            var atlas = context.Atlas;
            var path = context.AssetPath;
            var standards = context.Standards;
            var targets = MiniGameProfile.TextureTargets.Where(target =>
                AtlasPlatformSettings.Effective(atlas, target).maxTextureSize > standards.maxAtlasSize).ToArray();
            if (targets.Length == 0) return;
            context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.Texture,
                Id, "图集页面尺寸配置", string.Join(" / ", targets.Select(target =>
                    AtlasPlatformSettings.Effective(atlas, target).maxTextureSize).Distinct()),
                "≤ " + standards.maxAtlasSize, true, targets: targets));
        }

        public void Fix(TextureCheckContext context, AssetCheckIssue issue)
        {
            var atlas = context.Atlas;
            var standards = context.Standards;
            foreach (var target in issue.Targets)
            {
                var settings = AtlasPlatformSettings.Effective(atlas, target);
                settings.overridden = true;
                settings.maxTextureSize = standards.maxAtlasSize;
                atlas.SetPlatformSettings(settings);
            }
        }
    }

    internal sealed class AtlasReadWriteRule : IAssetCheckFix<TextureCheckContext>
    {
        public string Id => "atlas.read-write";

        public void Check(TextureCheckContext context)
        {
            if (!context.IsAtlas) return;
            var atlas = context.Atlas;
            var path = context.AssetPath;
            var standards = context.Standards;
            if (!standards.allowAtlasReadWrite && atlas.GetTextureSettings().readable)
                context.Add(new AssetCheckIssue(path, AssetCheckTarget.Id(path), AssetCheckKind.Texture, Id,
                    "图集 Read/Write", "开启", "禁止（所有平台）", true));
        }

        public void Fix(TextureCheckContext context, AssetCheckIssue issue)
        {
            var atlas = context.Atlas;
            var settings = atlas.GetTextureSettings();
            settings.readable = false;
            atlas.SetTextureSettings(settings);
        }
    }
}
