using UnityEditor;
using UnityEditor.U2D;

namespace July.AssetCheck
{
    internal sealed class TextureMipMapRule : IAssetCheckRule<TextureCheckContext>
    {
        private AtlasSourceTextures _sources;

        public void Check(TextureCheckContext context)
        {
            if (context.IsAtlas)
            {
                if (context.Standards.allowAtlasMipMaps) return;
                var atlas = context.Atlas.isVariant ? context.Atlas.GetMasterAtlas() : context.Atlas;
                if (atlas == null)
                {
                    context.Add(new AssetCheckIssue(context.AssetPath, AssetCheckTarget.Id(context.AssetPath), AssetCheckKind.Texture,
                        "atlas.mipmaps", "图集 MipMap", "变体主图集缺失", "有效主图集并关闭 MipMap", false, true));
                    return;
                }
                if (atlas.GetTextureSettings().generateMipMaps)
                    context.Add(new AssetCheckIssue(context.AssetPath, AssetCheckTarget.Id(context.AssetPath), AssetCheckKind.Texture,
                        "atlas.mipmaps", "图集 MipMap", context.Atlas.isVariant ? "开启（沿用主图集）" : "开启", "关闭", false));
                return;
            }
            var importer = context.Importer;
            if (context.Standards.allowSpriteMipMaps || importer.textureType != TextureImporterType.Sprite || !importer.mipmapEnabled) return;
            // 与源图压缩策略无关：即使不要求源图无压缩，图集仍接管其 MipMap 设置。
            if (_sources == null || _sources.Revision != AssetCheckAssetChanges.Revision) _sources = new AtlasSourceTextures();
            if (_sources.Contains(importer.assetPath)) return;
            context.Add(new AssetCheckIssue(context.AssetPath, AssetCheckTarget.Id(context.AssetPath), AssetCheckKind.Texture,
                "texture.sprite-mipmaps", "Sprite MipMap", "开启", "关闭", false));
        }
    }
}
