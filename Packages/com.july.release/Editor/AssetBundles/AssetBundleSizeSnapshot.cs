using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace July.Release.Editor
{
    public enum AssetBundleSizeCoverage { Unknown, Complete, Partial }

    public readonly struct AssetBundleResourceSize
    {
        public long Bytes { get; }
        public AssetBundleSizeCoverage Coverage { get; }
        public string Reason { get; }
        internal AssetBundleResourceSize(long bytes, AssetBundleSizeCoverage coverage, string reason)
        { Bytes = bytes; Coverage = coverage; Reason = reason; }
        public static AssetBundleResourceSize Unknown => new AssetBundleResourceSize(0, AssetBundleSizeCoverage.Unknown, "对应构建没有该资源的完整大小统计");
    }

    [Serializable] internal sealed class BundleSizeFile
    {
        public int schema;
        public string reportHash;
        public BundleSizeRecord[] bundles;
    }
    [Serializable] internal sealed class BundleSizeRecord
    {
        public string name;
        public long unpackedBytes, unassignedBytes;
        public ResourceSizeRecord[] resources;
        public GeneratedSizeRecord[] generated;
    }
    [Serializable] internal sealed class ResourceSizeRecord
    {
        public string guid, path, reason;
        public long bytes;
        public AssetBundleSizeCoverage coverage;
    }
    [Serializable] internal sealed class GeneratedSizeRecord { public string description; public long bytes; }

    public sealed class AssetBundleGeneratedSize
    {
        public string Description { get; }
        public long Bytes { get; }
        internal AssetBundleGeneratedSize(GeneratedSizeRecord data) { Description = data.description; Bytes = data.bytes; }
    }

    public sealed class AssetBundleWrittenSize
    {
        readonly Dictionary<string, ResourceSizeRecord> _resources;
        public string Name { get; }
        public long UnpackedBytes { get; }
        public long UnassignedBytes { get; }
        public IReadOnlyList<AssetBundleGeneratedSize> Generated { get; }
        internal AssetBundleWrittenSize(BundleSizeRecord data)
        {
            Name = data.name; UnpackedBytes = data.unpackedBytes; UnassignedBytes = data.unassignedBytes;
            _resources = data.resources.ToDictionary(r => Identity(r.guid, r.path), StringComparer.Ordinal);
            Generated = Array.AsReadOnly(data.generated.Select(r => new AssetBundleGeneratedSize(r)).ToArray());
        }
        internal static string Identity(string guid, string path) => string.IsNullOrEmpty(guid) || guid.All(c => c == '0') ? "path:" + path : "guid:" + guid.ToLowerInvariant();
        public AssetBundleResourceSize Resource(string guid, string path) => _resources.TryGetValue(Identity(guid, path), out var value)
            ? new AssetBundleResourceSize(value.bytes, value.coverage, value.reason) : AssetBundleResourceSize.Unknown;
    }

    // 构建事实只读快照。报告指纹保证旧报告不能误用另一轮构建的大小数据。
    public sealed class AssetBundleSizeSnapshot
    {
        readonly Dictionary<string, AssetBundleWrittenSize> _bundles;
        public IReadOnlyList<AssetBundleWrittenSize> Bundles { get; }
        internal AssetBundleSizeSnapshot(BundleSizeFile data)
        {
            _bundles = data.bundles.ToDictionary(b => b.name, b => new AssetBundleWrittenSize(b), StringComparer.Ordinal);
            Bundles = Array.AsReadOnly(_bundles.Values.ToArray());
        }
        public AssetBundleWrittenSize Bundle(string name) => _bundles[name];
        public AssetBundleResourceSize Resource(string bundle, string guid, string path) => _bundles[bundle].Resource(guid, path);
        public AssetBundleResourceSize Resource(IEnumerable<string> owners, string guid, string path)
        {
            var sizes = owners.Distinct(StringComparer.Ordinal).Select(owner => Resource(owner, guid, path)).ToArray();
            if (sizes.Length == 0 || sizes.All(s => s.Coverage == AssetBundleSizeCoverage.Unknown)) return AssetBundleResourceSize.Unknown;
            var coverage = sizes.All(s => s.Coverage == AssetBundleSizeCoverage.Complete) ? AssetBundleSizeCoverage.Complete : AssetBundleSizeCoverage.Partial;
            long bytes = 0; foreach (var size in sizes) bytes = checked(bytes + size.Bytes);
            return new AssetBundleResourceSize(bytes, coverage, string.Join("；", sizes.Where(s => s.Coverage != AssetBundleSizeCoverage.Complete).SelectMany(s => s.Reason.Split('；')).Distinct()));
        }
        public static string FilePath(string reportPath) => reportPath + ".sizes.json";
        public static AssetBundleSizeSnapshot ReadIfPresent(string reportPath)
        {
            var path = FilePath(reportPath);
            if (!File.Exists(path)) return null; // 已有旧报告没有逐资源统计，是正常输入。
            return ReadJson(File.ReadAllText(path), Hash(reportPath));
        }
        // Archive readers supply the SHA256 of the original report bytes; the same validation serves file and archived inputs.
        public static AssetBundleSizeSnapshot ReadJson(string json, string reportHash)
        {
            if (reportHash == null || reportHash.Length != 64 || !reportHash.All(Uri.IsHexDigit))
                throw new ArgumentException("必须提供原始 report 字节的 SHA256。", nameof(reportHash));
            BundleSizeFile data;
            try { data = JsonUtility.FromJson<BundleSizeFile>(json); }
            catch (ArgumentException exception) { throw new InvalidDataException("资源大小快照 JSON 无法解析。", exception); }
            if (data == null || data.schema != 1 || data.reportHash != reportHash || data.bundles == null)
                throw new InvalidDataException("资源大小快照版本或来源报告不匹配。请读取该次构建对应的 report 和大小快照。");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bundle in data.bundles)
            {
                if (bundle == null || string.IsNullOrWhiteSpace(bundle.name) || !names.Add(bundle.name) || bundle.unpackedBytes < 0 || bundle.unassignedBytes < 0 || bundle.resources == null || bundle.generated == null)
                    throw new InvalidDataException("资源大小快照包含无效或重复的包记录。");
                long total = bundle.unassignedBytes;
                var resources = new HashSet<string>(StringComparer.Ordinal);
                foreach (var resource in bundle.resources)
                {
                    if (resource == null || string.IsNullOrWhiteSpace(resource.path) || resource.bytes < 0 || !Enum.IsDefined(typeof(AssetBundleSizeCoverage), resource.coverage) || resource.coverage == AssetBundleSizeCoverage.Unknown ||
                        (resource.coverage == AssetBundleSizeCoverage.Partial && string.IsNullOrWhiteSpace(resource.reason)) ||
                        (!string.IsNullOrEmpty(resource.guid) && !Guid.TryParseExact(resource.guid, "N", out _)) || !resources.Add(AssetBundleWrittenSize.Identity(resource.guid, resource.path)))
                        throw new InvalidDataException("资源大小快照包含无效或重复的资源记录。");
                    total = checked(total + resource.bytes);
                }
                foreach (var generated in bundle.generated)
                {
                    if (generated == null || generated.bytes < 0 || string.IsNullOrWhiteSpace(generated.description)) throw new InvalidDataException("生成数据大小记录无效。");
                    total = checked(total + generated.bytes);
                }
                if (total != bundle.unpackedBytes) throw new InvalidDataException("资源大小快照与包内未压缩数据合计不一致。");
            }
            return new AssetBundleSizeSnapshot(data);
        }
        internal static void Write(string reportPath, BundleSizeRecord[] bundles)
        {
            var data = new BundleSizeFile { schema = 1, reportHash = Hash(reportPath), bundles = bundles };
            // 本次输出目录由 YooAsset 保证尚未存在；完成统计后才发布最终快照文件。
            File.WriteAllText(FilePath(reportPath), JsonUtility.ToJson(data, true));
        }
        internal static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
