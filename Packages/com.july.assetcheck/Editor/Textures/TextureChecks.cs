using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace July.AssetCheck
{
    internal sealed class TextureSizeRule : IAssetCheckFix<TextureCheckContext>
    {
        public string Id => "texture.imported-size";

        public void Check(TextureCheckContext context)
        {
            if (context.IsAtlas) return;
            var importer = context.Importer;
            var standards = context.Standards;
            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            // 判断目标平台导入尺寸的长边是否超出预算，不能仅因小图的 Max Size 设置较大
            // 就认定导入后的贴图尺寸超标。
            var sourceEdge = Math.Max(Scale(width, importer.npotScale), Scale(height, importer.npotScale));
            var targets = new List<string>();
            var values = new List<int>();
            foreach (var target in MiniGameProfile.TextureTargets)
            {
                var settings = importer.GetPlatformTextureSettings(target);
                var maximum = settings.overridden ? settings.maxTextureSize : importer.GetDefaultPlatformTextureSettings().maxTextureSize;
                var edge = Math.Min(sourceEdge, maximum);
                if (edge <= standards.maxTextureSize)
                    continue;
                targets.Add(target);
                values.Add(edge);
            }
            if (targets.Count == 0) return;
            context.Add(new AssetCheckIssue(importer.assetPath,
                AssetCheckTarget.Id(importer.assetPath), AssetCheckKind.Texture, Id, "导入长边尺寸",
                string.Join(" / ", values.Distinct()), "≤ " + standards.maxTextureSize, true, targets: targets));
        }

        public void Fix(TextureCheckContext context, AssetCheckIssue issue)
        {
            var importer = context.Importer;
            var standards = context.Standards;
            foreach (var target in issue.Targets)
            {
                if (target != "WebGL")
                    TuanjieTextureSettings.WriteMaxSize(importer, target, standards.maxTextureSize);
                else
                {
                    var settings = importer.GetPlatformTextureSettings(target);
                    if (!settings.overridden)
                        importer.GetDefaultPlatformTextureSettings().CopyTo(settings);
                    settings.name = target;
                    settings.overridden = true;
                    settings.maxTextureSize = standards.maxTextureSize;
                    importer.SetPlatformTextureSettings(settings);
                }
            }
        }

        private static int Scale(int value, TextureImporterNPOTScale scale)
        {
            switch (scale)
            {
                case TextureImporterNPOTScale.ToNearest: return Mathf.ClosestPowerOfTwo(value);
                case TextureImporterNPOTScale.ToLarger: return Mathf.NextPowerOfTwo(value);
                case TextureImporterNPOTScale.ToSmaller:
                    var upper = Mathf.NextPowerOfTwo(value);
                    return upper == value ? upper : upper / 2;
                default: return value;
            }
        }
    }

    internal sealed class TextureReadWriteRule : IAssetCheckFix<TextureCheckContext>
    {
        public string Id => "texture.read-write";

        public void Check(TextureCheckContext context)
        {
            if (context.IsAtlas) return;
            var importer = context.Importer;
            var standards = context.Standards;
            if (!standards.allowReadWrite && importer.isReadable)
                context.Add(new AssetCheckIssue(importer.assetPath, AssetCheckTarget.Id(importer.assetPath), AssetCheckKind.Texture,
                    Id, "Read/Write", "开启", "禁止（所有平台）", true));
        }

        public void Fix(TextureCheckContext context, AssetCheckIssue issue) => context.Importer.isReadable = false;
    }

    internal sealed class TextureFormatRule : IAssetCheckFix<TextureCheckContext>
    {
        public string Id => "texture.compression-format";

        public void Check(TextureCheckContext context)
        {
            if (context.IsAtlas) return;
            var importer = context.Importer;
            var standards = context.Standards;
            var targets = new List<string>();
            var values = new List<string>();
            var unverified = false;
            var hasProblem = false;
            var unknownTargets = new List<string>();
            foreach (var target in MiniGameProfile.TextureTargets)
            {
                var format = GetEffectiveFormat(importer, target);
                if (standards.allowedFormats.Contains(format))
                    continue;
                targets.Add(target);
                unverified |= format == TextureImporterFormat.Automatic;
                if (format == TextureImporterFormat.Automatic) unknownTargets.Add(target);
                hasProblem |= format != TextureImporterFormat.Automatic;
                values.Add(format == TextureImporterFormat.Automatic ? "Automatic（未能验证）" : FormatName(format));
            }
            if (targets.Count == 0)
                return;

            context.Add(new AssetCheckIssue(importer.assetPath, AssetDatabase.AssetPathToGUID(importer.assetPath),
                AssetCheckKind.Texture, Id, "压缩格式", string.Join(" / ", values.Distinct()),
                string.Join(" / ", standards.allowedFormats.Select(FormatName)), true, unverified,
                targets: targets, hasProblem: hasProblem, unverifiedTargets: unknownTargets));
        }

        public void Fix(TextureCheckContext context, AssetCheckIssue issue)
        {
            var importer = context.Importer;
            var standards = context.Standards;
            foreach (var target in issue.Targets)
            {
                if (target != "WebGL")
                    TuanjieTextureSettings.WriteFormat(importer, target, standards.compressionFormat);
                else
                {
                    var settings = importer.GetPlatformTextureSettings(target);
                    if (!settings.overridden)
                        importer.GetDefaultPlatformTextureSettings().CopyTo(settings);
                    settings.name = target;
                    settings.overridden = true;
                    settings.format = standards.compressionFormat;
                    settings.textureCompression = TextureImporterCompression.Compressed;
                    importer.SetPlatformTextureSettings(settings);
                }

                var written = importer.GetPlatformTextureSettings(target);
                if (!written.overridden || written.format != standards.compressionFormat)
                    throw new InvalidOperationException($"当前编辑器未能写入 {target} 纹理设置：{importer.assetPath}");
            }
        }

        internal static TextureImporterFormat GetEffectiveFormat(TextureImporter importer, string target)
        {
            var settings = importer.GetPlatformTextureSettings(target);
            if (settings.overridden && settings.format != TextureImporterFormat.Automatic)
                return settings.format;

            return importer.GetAutomaticFormat(target);
        }

        internal static string FormatName(TextureImporterFormat format) => format.ToString().Replace("_", " ");
    }

    internal sealed class TextureCheckModule : IAssetCheckModule
    {
        private readonly IAssetCheckRule<TextureCheckContext>[] _rules = AssetCheckRules<TextureCheckContext>.Create();
        public AssetCheckKind Kind => AssetCheckKind.Texture;
        public string RulesFingerprint => AssetCheckRules<TextureCheckContext>.Fingerprint;

        public string[] Collect(string scope) => AssetDatabase.FindAssets("t:Texture2D", new[] { scope })
            .Concat(AssetDatabase.FindAssets("t:SpriteAtlas", new[] { scope })).Distinct().ToArray();

        public List<AssetCheckIssue> Check(string assetPath, PlatformStandards standards)
        {
            var issues = new List<AssetCheckIssue>();
            var atlas = AssetDatabase.LoadMainAssetAtPath(assetPath) as SpriteAtlas;
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (atlas == null && importer == null)
            {
                issues.Add(new AssetCheckIssue(assetPath, AssetDatabase.AssetPathToGUID(assetPath), Kind,
                    "texture.importer", "未能验证", "该贴图没有 TextureImporter", "支持的贴图导入资源", false, true));
                return issues;
            }
            AssetCheckRules<TextureCheckContext>.Check(_rules,
                new TextureCheckContext(assetPath, importer, atlas, standards.texture, issues));
            return issues;
        }

        public void Fix(string assetPath, IReadOnlyList<AssetCheckIssue> issues, PlatformStandards standards)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) is SpriteAtlas atlas)
            {
                var atlasContext = new TextureCheckContext(assetPath, null, atlas, standards.texture, new List<AssetCheckIssue>());
                foreach (var issue in issues) AssetCheckRules<TextureCheckContext>.Fix(atlasContext, issue);
                EditorUtility.SetDirty(atlas);
                AssetDatabase.SaveAssetIfDirty(atlas);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                return;
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            var textureContext = new TextureCheckContext(assetPath, importer, null, standards.texture, new List<AssetCheckIssue>());
            foreach (var issue in issues) AssetCheckRules<TextureCheckContext>.Fix(textureContext, issue);
            importer.SaveAndReimport();
        }
    }
}
