using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine.U2D;

namespace July.AssetCheck
{
    // 与检查标准无关的一次性复制任务；每批结束后恢复导入，再交还编辑器。
    internal sealed class WebGLPlatformSettingsCopy
    {
        private readonly string[] _guids;
        private readonly Func<AssetCheckSettings> _settings;
        internal int Total => _guids.Length;
        internal int Processed { get; private set; }
        internal int Modified { get; private set; }
        internal int Ignored { get; private set; }
        internal int Unchanged => Processed - Modified - Ignored;
        internal bool Cancelled { get; private set; }
        internal bool Running => !Cancelled && Processed < Total;

        internal WebGLPlatformSettingsCopy(string[] paths, Func<AssetCheckSettings> settings)
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
                    foreach (var guid in AssetDatabase.FindAssets("t:Texture2D t:SpriteAtlas t:AudioClip", new[] { scope }))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (Supported(path)) paths.Add(path);
                    }
                }
                else if (Supported(scope)) paths.Add(scope);
                else throw new ArgumentException("请选择文件夹、导入贴图、SpriteAtlas 或音频：" + scope);
            }
            return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        private static bool Supported(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            return importer is TextureImporter or AudioImporter ||
                AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(SpriteAtlas);
        }

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
            entry.Apply();
            return true;
        }

        internal static WebGLPlatformSettingsCopyEntry Inspect(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer is AudioImporter audio)
                return new AudioPlatformSettingsCopyEntry(audio);
            if (importer is TextureImporter texture)
                return TexturePlatformSettingsCopyEntry.Inspect(texture);
            if (AssetDatabase.LoadMainAssetAtPath(path) is SpriteAtlas atlas)
                return TexturePlatformSettingsCopyEntry.Inspect(path, atlas);
            throw new InvalidDataException("资源已不再是可复制的导入贴图、SpriteAtlas 或音频：" + path);
        }
    }
}
