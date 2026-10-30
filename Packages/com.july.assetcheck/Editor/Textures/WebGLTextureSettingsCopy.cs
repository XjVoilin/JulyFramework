using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine.U2D;

namespace July.AssetCheck
{
    // 与检查标准无关的一次性复制任务；每批结束后恢复导入，再交还编辑器。
    internal sealed class WebGLTextureSettingsCopy
    {
        private static readonly string[] Targets = { "MiniGame", "WeixinMiniGame" };
        private readonly string[] _guids;
        private readonly Func<AssetCheckSettings> _settings;
        internal int Total => _guids.Length;
        internal int Processed { get; private set; }
        internal int Modified { get; private set; }
        internal int Ignored { get; private set; }
        internal int Unchanged => Processed - Modified - Ignored;
        internal bool Cancelled { get; private set; }
        internal bool Running => !Cancelled && Processed < Total;

        internal WebGLTextureSettingsCopy(string[] paths, Func<AssetCheckSettings> settings)
        {
            _settings = settings;
            // 跟踪 GUID，批次之间移动资源时仍指向原文件；删除则明确失败。
            _guids = paths.Select(AssetDatabase.AssetPathToGUID).ToArray();
        }

        internal static string[] Collect(string[] scopes)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scope in scopes)
            {
                if (scope != "Assets" && !scope.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new ArgumentException("复制范围必须位于 Assets：" + scope);
                if (AssetDatabase.IsValidFolder(scope))
                {
                    foreach (var guid in AssetDatabase.FindAssets("t:Texture2D t:SpriteAtlas", new[] { scope }))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (Supported(path)) paths.Add(path);
                    }
                }
                else if (Supported(scope)) paths.Add(scope);
                else throw new ArgumentException("请选择文件夹、导入贴图或 SpriteAtlas：" + scope);
            }
            return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        private static bool Supported(string path) => AssetImporter.GetAtPath(path) is TextureImporter ||
            AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(SpriteAtlas);

        internal void Cancel() => Cancelled = true;

        internal void Step()
        {
            if (!Running) return;
            var end = Math.Min(Processed + 20, Total);
            AssetDatabase.StartAssetEditing();
            try
            {
                while (Processed < end)
                {
                    var path = AssetCheckTarget.RequirePath(_guids[Processed]);
                    var exclusions = new AssetCheckExclusions(_settings());
                    if (exclusions.Contains(_guids[Processed], path)) Ignored++;
                    else if (CopyAsset(path)) Modified++;
                    Processed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        internal static bool CopyAsset(string path)
        {
            // 与扫描共用差异判断，执行前重读当前配置。
            var entry = Inspect(path);
            if (!entry.NeedsCopy) return false;
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                foreach (var target in Targets)
                {
                    if (target == "MiniGame" ? entry.CopyMiniGame : entry.CopyWeixinMiniGame)
                        TuanjieTextureSettings.WriteSettings(importer, entry.ForTarget(target));
                }
                importer.SaveAndReimport();
                return true;
            }
            var atlas = (SpriteAtlas)AssetDatabase.LoadMainAssetAtPath(path);
            foreach (var target in Targets)
            {
                if (target == "MiniGame" ? entry.CopyMiniGame : entry.CopyWeixinMiniGame)
                    atlas.SetPlatformSettings(entry.ForTarget(target));
            }
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssetIfDirty(atlas);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return true;
        }

        internal static WebGLTextureSettingsCopyEntry Inspect(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                var source = importer.GetPlatformTextureSettings("WebGL");
                var inherited = !source.overridden;
                if (inherited) importer.GetDefaultPlatformTextureSettings().CopyTo(source);
                return new WebGLTextureSettingsCopyEntry(path, false, inherited, source,
                    importer.GetPlatformTextureSettings("MiniGame"), importer.GetPlatformTextureSettings("WeixinMiniGame"));
            }
            if (AssetDatabase.LoadMainAssetAtPath(path) is SpriteAtlas atlas)
                return new WebGLTextureSettingsCopyEntry(path, true, !atlas.GetPlatformSettings("WebGL").overridden,
                    AtlasPlatformSettings.Effective(atlas, "WebGL"), atlas.GetPlatformSettings("MiniGame"), atlas.GetPlatformSettings("WeixinMiniGame"));
            throw new InvalidDataException("资源已不再是导入贴图或 SpriteAtlas：" + path);
        }
    }
}
