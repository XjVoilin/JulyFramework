using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine.U2D;

namespace July.AssetCheck
{
    // 归属索引由贴图 Module 持有；资源变化后重建，不依赖已经打包的缓存。
    internal sealed class AtlasSourceTextures
    {
        private readonly HashSet<string> _textures = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _folders = new HashSet<string>(StringComparer.Ordinal);
        internal int Revision { get; } = AssetCheckAssetChanges.Revision;

        internal AtlasSourceTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AssetDatabase.GUIDToAssetPath(guid));
                // 变体沿用主图集的打包对象，主图集会由同一次查询收集。
                if (atlas.isVariant) continue;
                foreach (var packable in atlas.GetPackables())
                {
                    // 已删除的打包对象不建立归属，完整性问题由其所有者处理。
                    if (packable == null) continue;
                    var path = AssetDatabase.GetAssetPath(packable);
                    if (AssetDatabase.IsValidFolder(path)) _folders.Add(path + "/");
                    else _textures.Add(path);
                }
            }
        }

        internal bool Contains(string path)
        {
            if (_textures.Contains(path)) return true;
            foreach (var folder in _folders)
                if (path.StartsWith(folder, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
