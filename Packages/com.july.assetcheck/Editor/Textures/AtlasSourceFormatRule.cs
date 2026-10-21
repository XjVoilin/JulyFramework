using System;
using System.Collections.Generic;
using UnityEditor;

namespace July.AssetCheck
{
    internal sealed class AtlasSourceFormatRule : IAssetCheckFix<TextureCheckContext>
    {
        private const string DefaultTarget = "DefaultTexturePlatform";
        public string Id => "texture.atlas-source-format";

        public void Check(TextureCheckContext context)
        {
            if (!context.Standards.requireUncompressedAtlasSources || !context.IsAtlasSource) return;
            var importer = context.Importer;
            var targets = new List<string>();
            var values = new List<string>();
            CheckSettings(DefaultTarget, importer.GetDefaultPlatformTextureSettings(), targets, values);
            foreach (var target in MiniGameProfile.TextureTargets)
            {
                var settings = importer.GetPlatformTextureSettings(target);
                if (!settings.overridden) settings = importer.GetDefaultPlatformTextureSettings();
                CheckSettings(target, settings, targets, values);
            }
            if (targets.Count == 0) return;
            context.Add(new AssetCheckIssue(context.AssetPath, AssetCheckTarget.Id(context.AssetPath), AssetCheckKind.Texture,
                Id, "图集源图格式", string.Join(" / ", values), "无压缩（修改为 RGBA32）", true, targets: targets));
        }

        private static void CheckSettings(string target, TextureImporterPlatformSettings settings,
            List<string> targets, List<string> values)
        {
            if (settings.format == TextureImporterFormat.Automatic
                ? settings.textureCompression == TextureImporterCompression.Uncompressed
                : IsUncompressed(settings.format)) return;
            targets.Add(target);
            values.Add((target == DefaultTarget ? "Default" : target) + "：" +
                       (settings.format == TextureImporterFormat.Automatic
                           ? "Automatic / " + settings.textureCompression
                           : TextureFormatRule.FormatName(settings.format)));
        }

        private static bool IsUncompressed(TextureImporterFormat format)
        {
            switch (format)
            {
                case TextureImporterFormat.Alpha8:
                case TextureImporterFormat.ARGB16:
                case TextureImporterFormat.RGB24:
                case TextureImporterFormat.RGBA32:
                case TextureImporterFormat.ARGB32:
                case TextureImporterFormat.RGB16:
                case TextureImporterFormat.R16:
                case TextureImporterFormat.RGBA16:
                case TextureImporterFormat.RHalf:
                case TextureImporterFormat.RGHalf:
                case TextureImporterFormat.RGBAHalf:
                case TextureImporterFormat.RFloat:
                case TextureImporterFormat.RGFloat:
                case TextureImporterFormat.RGBAFloat:
                case TextureImporterFormat.RGB9E5:
                case TextureImporterFormat.RG16:
                case TextureImporterFormat.R8:
                case TextureImporterFormat.RG32:
                case TextureImporterFormat.RGB48:
                case TextureImporterFormat.RGBA64:
                    return true;
                default:
                    return false;
            }
        }

        public void Fix(TextureCheckContext context, AssetCheckIssue issue)
        {
            var importer = context.Importer;
            foreach (var target in issue.Targets)
            {
                var settings = target == DefaultTarget
                    ? importer.GetDefaultPlatformTextureSettings()
                    : importer.GetPlatformTextureSettings(target);
                if (target != DefaultTarget && !settings.overridden)
                    importer.GetDefaultPlatformTextureSettings().CopyTo(settings);
                settings.name = target;
                settings.overridden = target != DefaultTarget;
                settings.format = TextureImporterFormat.RGBA32;
                settings.textureCompression = TextureImporterCompression.Uncompressed;
                settings.crunchedCompression = false;
                importer.SetPlatformTextureSettings(settings);
                var written = importer.GetPlatformTextureSettings(target);
                if (written.format != TextureImporterFormat.RGBA32 ||
                    written.textureCompression != TextureImporterCompression.Uncompressed)
                    throw new InvalidOperationException($"当前编辑器未能写入 {target} 源图无压缩设置：{context.AssetPath}");
            }
        }
    }
}
