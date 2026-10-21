using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static July.AssetCheck.AssetCheckStandardFields;

namespace July.AssetCheck
{
    internal static class TextureStandardsView
    {
        internal static void Build(VisualElement line, PlatformStandards standards, Action<Action<PlatformStandards>> edit)
        {
            TextureFormatStandardsView.Build(line, "贴图", standards.texture.allowedFormats, standards.texture.compressionFormat,
                v => edit(s => s.texture.allowedFormats = v), v => edit(s => s.texture.compressionFormat = v));
            var atlasSources = new Toggle("图集源图使用无压缩")
            {
                value = standards.texture.requireUncompressedAtlasSources,
                tooltip = "按 SpriteAtlas 打包对象识别源图；勾选后源图检查无压缩，其他贴图仍按允许格式检查。源图格式检查 Default 和目标平台，尺寸及 Read/Write 规则保持。"
            };
            atlasSources.RegisterValueChangedCallback(e => edit(s => s.texture.requireUncompressedAtlasSources = e.newValue));
            Add(line, atlasSources);
            AddSize(line, "贴图长边上限", standards.texture.maxTextureSize, v => edit(s => s.texture.maxTextureSize = v));
            AddPermission(line, "贴图 Read/Write", standards.texture.allowReadWrite, v => edit(s => s.texture.allowReadWrite = v));
            TextureFormatStandardsView.Build(line, "图集", standards.texture.allowedAtlasFormats, standards.texture.atlasCompressionFormat,
                v => edit(s => s.texture.allowedAtlasFormats = v), v => edit(s => s.texture.atlasCompressionFormat = v));
            AddSize(line, "图集页面上限", standards.texture.maxAtlasSize, v => edit(s => s.texture.maxAtlasSize = v));
            AddPermission(line, "图集 Read/Write", standards.texture.allowAtlasReadWrite, v => edit(s => s.texture.allowAtlasReadWrite = v));
        }
    }
}
