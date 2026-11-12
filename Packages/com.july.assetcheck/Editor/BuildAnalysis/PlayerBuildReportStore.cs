using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace July.AssetCheck
{
    [Serializable]
    internal sealed class PlayerBuildAsset
    {
        public string guid, path, type;
        public ulong bytes;
    }

    [Serializable]
    internal sealed class PlayerBuildSnapshot
    {
        public int version = 2;
        public string platform, builtAt, unityVersion, outputPath;
        public bool development, cleanBuild, scriptsOnly;
        public ulong totalBytes, assetBytes;
        public PlayerBuildAsset[] assets;
    }

    [Serializable]
    internal sealed class PlayerBuildCache
    {
        public int version = 1;
        public string lastBuiltAt, lastOutputPath;
        public bool lastHadDetails;
        public PlayerBuildSnapshot baseline, incremental;

        internal PlayerBuildSnapshot LatestDetails => incremental ?? baseline;
    }

    internal sealed class PlayerBuildReportStore
    {
        internal const string RelativePath = "July/AssetCheck/PlayerBuild";
        internal const string LegacyPath = "Library/July/AssetCheck/PlayerBuild/latest.json";
        internal static event Action Changed;
        private readonly string _project;
        internal string DirectoryPath => Path.Combine(_project, RelativePath);

        internal PlayerBuildReportStore(string projectDirectory) => _project = Path.GetFullPath(projectDirectory);

        internal IReadOnlyDictionary<string, PlayerBuildCache> LoadAvailable()
        {
            var platforms = new HashSet<string>(StringComparer.Ordinal);
            if (Directory.Exists(DirectoryPath))
                foreach (var path in Directory.GetFiles(DirectoryPath, "*.json"))
                    platforms.Add(Path.GetFileNameWithoutExtension(path));
            var legacyPath = Path.Combine(_project, LegacyPath);
            if (File.Exists(legacyPath))
            {
                var legacy = Read<PlayerBuildSnapshot>(legacyPath);
                if (legacy == null) throw Invalid(legacyPath);
                platforms.Add(legacy.platform);
            }
            return platforms.ToDictionary(platform => platform, Load, StringComparer.Ordinal);
        }

        internal static PlayerBuildSnapshot Capture(BuildReport report)
        {
            var assets = new Dictionary<string, PlayerBuildAsset>(StringComparer.Ordinal);
            var types = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var packed in report.packedAssets)
            foreach (var content in packed.contents)
            {
                var guid = content.sourceAssetGUID.ToString();
                if (guid == "00000000000000000000000000000000") guid = "";
                var path = string.IsNullOrEmpty(content.sourceAssetPath) ? "Unity 内部生成数据" : content.sourceAssetPath;
                var key = guid.Length > 0 ? "guid:" + guid : "path:" + path;
                if (!assets.TryGetValue(key, out var asset))
                {
                    assets.Add(key, asset = new PlayerBuildAsset { guid = guid, path = path });
                    types.Add(key, new SortedSet<string>(StringComparer.Ordinal));
                }
                // 保留同一源资源在不同数据文件中的物理占用，不按 GUID 丢弃副本。
                asset.bytes = checked(asset.bytes + content.packedSize);
                types[key].Add(content.type?.Name ?? "内部数据");
            }
            ulong bytes = 0;
            foreach (var pair in assets)
            {
                pair.Value.type = string.Join(" / ", types[pair.Key]);
                bytes = checked(bytes + pair.Value.bytes);
            }
            var summary = report.summary;
            return new PlayerBuildSnapshot
            {
                platform = summary.platform.ToString(), builtAt = summary.buildEndedAt.ToString("O"),
                unityVersion = Application.unityVersion, outputPath = summary.outputPath,
                development = (summary.options & BuildOptions.Development) != 0,
                cleanBuild = (summary.options & BuildOptions.CleanBuildCache) != 0,
                scriptsOnly = EditorUserBuildSettings.buildScriptsOnly || (summary.options & BuildOptions.BuildScriptsOnly) != 0,
                totalBytes = summary.totalSize, assetBytes = bytes,
                assets = assets.Values.OrderByDescending(asset => asset.bytes).ThenBy(asset => asset.path, StringComparer.Ordinal).ToArray()
            };
        }

        // Only successful content builds may update client resource records.
        internal bool Record(PlayerBuildSnapshot snapshot, BuildResult result)
        {
            if (result != BuildResult.Succeeded || snapshot.scriptsOnly) return false;
            var cache = Load(snapshot.platform);
            cache.lastBuiltAt = snapshot.builtAt;
            cache.lastOutputPath = snapshot.outputPath;
            cache.lastHadDetails = snapshot.assets.Length > 0;
            if (cache.lastHadDetails)
            {
                if (snapshot.cleanBuild)
                {
                    cache.baseline = snapshot;
                    cache.incremental = null;
                }
                else cache.incremental = snapshot;
            }
            Directory.CreateDirectory(DirectoryPath);
            var path = PathFor(snapshot.platform);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(cache, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            Changed?.Invoke();
            return true;
        }

        internal PlayerBuildCache Load(string platform)
        {
            var path = PathFor(platform);
            if (File.Exists(path))
            {
                var cache = Read<PlayerBuildCache>(path);
                if (cache == null || cache.version != 1 || string.IsNullOrEmpty(cache.lastBuiltAt)) throw Invalid(path);
                Validate(cache.baseline, platform, path);
                Validate(cache.incremental, platform, path);
                return cache;
            }
            // Read old records without changing them or claiming unrecorded build coverage.
            var legacyPath = Path.Combine(_project, LegacyPath);
            if (!File.Exists(legacyPath)) return new PlayerBuildCache();
            var legacy = Read<PlayerBuildSnapshot>(legacyPath);
            if (legacy == null) throw Invalid(legacyPath);
            if (legacy.platform != platform) return new PlayerBuildCache();
            Validate(legacy, platform, legacyPath);
            return new PlayerBuildCache
            {
                lastBuiltAt = legacy.builtAt, lastOutputPath = legacy.outputPath,
                lastHadDetails = legacy.assets.Length > 0,
                baseline = legacy.assets.Length > 0 ? legacy : null
            };
        }

        private string PathFor(string platform)
        {
            // Platform comes from a report/file boundary; never use arbitrary text as a filename.
            if (!Enum.TryParse<BuildTarget>(platform, out var target) || !Enum.IsDefined(typeof(BuildTarget), target))
                throw new InvalidDataException("Unknown Player build target: " + platform);
            return Path.Combine(DirectoryPath, target + ".json");
        }

        private static void Validate(PlayerBuildSnapshot snapshot, string platform, string path)
        {
            if (snapshot == null) return;
            if ((snapshot.version != 1 && snapshot.version != 2) || snapshot.platform != platform ||
                snapshot.assets == null || snapshot.assets.Any(asset => asset == null || string.IsNullOrEmpty(asset.path)))
                throw Invalid(path);
        }

        private static T Read<T>(string path)
        {
            try { return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)); }
            catch (JsonException exception) { throw new InvalidDataException("客户端构建资源记录无法解析：" + path, exception); }
        }

        private static InvalidDataException Invalid(string path) => new InvalidDataException("客户端构建资源记录无效：" + path);
    }

    // Capture during the callback; finalize only the last content report after the build finishes.
    internal sealed class PlayerBuildReportCapture : IPostprocessBuildWithReport
    {
        private sealed class PendingReport
        {
            internal BuildReport Report;
            internal PlayerBuildSnapshot Snapshot;
        }

        private static PendingReport _pending;
        public int callbackOrder => int.MaxValue;

        public void OnPostprocessBuild(BuildReport report)
        {
            var snapshot = PlayerBuildReportStore.Capture(report);
            // HybridCLR enables scriptsOnly while producing its temporary stripped-AOT Player.
            if (snapshot.scriptsOnly) return;
            _pending = new PendingReport { Report = report, Snapshot = snapshot };
            EditorApplication.update -= Complete;
            EditorApplication.update += Complete;
        }

        private static void Complete()
        {
            if (BuildPipeline.isBuildingPlayer) return;
            EditorApplication.update -= Complete;
            var pending = _pending;
            _pending = null;
            var summary = pending.Report.summary;
            var snapshot = pending.Snapshot;
            snapshot.builtAt = summary.buildEndedAt.ToString("O");
            snapshot.totalBytes = summary.totalSize;
            new PlayerBuildReportStore(Directory.GetCurrentDirectory()).Record(snapshot, summary.result);
        }
    }
}
