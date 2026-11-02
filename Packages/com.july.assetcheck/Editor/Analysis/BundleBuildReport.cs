using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using July.Release.Editor;

namespace July.AssetCheck
{
    // 只读取 YooAsset 2.3.x 的报告数据，不引用或执行构建管线。
    [Serializable]
    internal sealed class BundleReportData
    {
        public BundleReportSummary Summary;
        public BundleReportAssetData[] AssetInfos;
        public BundleReportBundleData[] BundleInfos;
    }

    [Serializable]
    internal sealed class BundleReportSummary
    {
        public string YooVersion, UnityVersion, BuildDate, BuildPackageName, BuildPackageVersion;
        public int BuildTarget, AllBundleTotalCount, MainAssetTotalCount;
        public long AllBundleTotalSize;
    }

    [Serializable]
    internal sealed class BundleReportDependencyData
    {
        public string AssetPath, AssetGUID;
    }

    [Serializable]
    internal sealed class BundleReportAssetData
    {
        public string AssetPath, AssetGUID, MainBundleName;
        public string[] DependBundles;
        public BundleReportDependencyData[] DependAssets;
    }

    [Serializable]
    internal sealed class BundleReportBundleData
    {
        public string BundleName, FileName;
        public long FileSize;
        public string[] Tags, DependBundles;
        public BundleReportAssetData[] BundleContents;
    }

    internal sealed class BundleBuildReport
    {
        internal string SourcePath { get; }
        internal string Signature { get; private set; }
        internal byte[] ReportBytes { get; private set; }
        internal string SizeJson { get; private set; }
        internal string SizeSignature { get; private set; }
        internal BundleReportData Data { get; }
        internal IReadOnlyDictionary<string, BundleReportBundleData> Bundles { get; }
        internal long TotalBytes { get; }
        internal AssetBundleSizeSnapshot Sizes { get; }

        private BundleBuildReport(string path, BundleReportData data, Dictionary<string, BundleReportBundleData> bundles, long bytes, AssetBundleSizeSnapshot sizes)
        {
            SourcePath = path; Data = data; Bundles = bundles; TotalBytes = bytes; Sizes = sizes;
        }

        internal static BundleBuildReport Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请先选择 .report 文件。");
            var sizePath = AssetBundleSizeSnapshot.FilePath(path);
            return ReadStored(path, File.ReadAllBytes(path), File.Exists(sizePath) ? File.ReadAllText(sizePath) : null);
        }

        internal static BundleBuildReport ReadStored(string path, byte[] reportBytes, string sizeJson)
        {
            string json;
            using (var reader = new StreamReader(new MemoryStream(reportBytes), Encoding.UTF8, true)) json = reader.ReadToEnd();
            var signature = Fingerprint(reportBytes);
            BundleReportData data;
            try { data = JsonUtility.FromJson<BundleReportData>(json); }
            catch (ArgumentException exception) { throw new InvalidDataException("报告 JSON 无法解析。", exception); }
            if (data?.Summary == null || data.AssetInfos == null || data.BundleInfos == null)
                throw new InvalidDataException("请选择 YooAsset 构建生成的 .report 文件，不能使用资源清单 JSON。");
            var summary = data.Summary;
            if (summary.YooVersion == null || !summary.YooVersion.StartsWith("2.3.", StringComparison.Ordinal))
                throw new NotSupportedException("当前支持 YooAsset 2.3.x 构建报告，报告版本：" + summary.YooVersion);
            if (string.IsNullOrWhiteSpace(summary.BuildPackageName) || string.IsNullOrWhiteSpace(summary.BuildPackageVersion) ||
                string.IsNullOrWhiteSpace(summary.BuildDate) || !Enum.IsDefined(typeof(BuildTarget), summary.BuildTarget))
                throw new InvalidDataException("报告缺少有效的 Package、版本、构建时间或平台。");

            var bundles = new Dictionary<string, BundleReportBundleData>(StringComparer.Ordinal);
            long bytes = 0;
            foreach (var bundle in data.BundleInfos)
            {
                if (bundle == null || string.IsNullOrWhiteSpace(bundle.BundleName) || bundle.FileSize < 0 ||
                    bundle.DependBundles == null || bundle.BundleContents == null || bundles.ContainsKey(bundle.BundleName))
                    throw new InvalidDataException("报告包含缺失、重复或无效的资源包数据。");
                foreach (var asset in bundle.BundleContents) ValidateAsset(asset?.AssetPath, asset?.AssetGUID);
                bundle.BundleContents = bundle.BundleContents.GroupBy(asset => asset.AssetPath, StringComparer.Ordinal).Select(group => group.First()).ToArray();
                bundle.DependBundles = bundle.DependBundles.Distinct(StringComparer.Ordinal).ToArray();
                bundle.Tags = bundle.Tags ?? Array.Empty<string>();
                bundles.Add(bundle.BundleName, bundle);
                try { bytes = checked(bytes + bundle.FileSize); }
                catch (OverflowException exception) { throw new InvalidDataException("报告资源包体积合计超出支持范围。", exception); }
            }
            foreach (var bundle in bundles.Values)
                foreach (var dependency in bundle.DependBundles) RequireBundle(bundles, dependency);
            var mainPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in data.AssetInfos)
            {
                ValidateAsset(asset?.AssetPath, asset?.AssetGUID);
                RequireBundle(bundles, asset.MainBundleName);
                if (asset.DependBundles == null || !mainPaths.Add(asset.AssetPath))
                    throw new InvalidDataException("报告包含重复的主资源或缺少主资源依赖数据。");
                foreach (var dependency in asset.DependBundles) RequireBundle(bundles, dependency);
                if (asset.DependAssets != null) foreach (var dependency in asset.DependAssets) ValidateAsset(dependency?.AssetPath, dependency?.AssetGUID);
            }
            if (summary.AllBundleTotalCount != bundles.Count || summary.AllBundleTotalSize != bytes || summary.MainAssetTotalCount != data.AssetInfos.Length)
                throw new InvalidDataException("报告汇总数量或体积与资源包、主资源列表不一致。");
            var sizes = sizeJson == null ? null : AssetBundleSizeSnapshot.ReadJson(sizeJson, signature.ToLowerInvariant());
            if (sizes != null && !new HashSet<string>(sizes.Bundles.Select(b => b.Name), StringComparer.Ordinal).SetEquals(bundles.Keys))
                throw new InvalidDataException("资源大小快照的包集合与报告不一致。");
            return new BundleBuildReport(Path.GetFullPath(path), data, bundles, bytes, sizes)
            {
                Signature = signature, ReportBytes = reportBytes, SizeJson = sizeJson,
                SizeSignature = sizeJson == null ? "" : Fingerprint(Encoding.UTF8.GetBytes(sizeJson))
            };
        }

        private static string Fingerprint(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        private static void RequireBundle(Dictionary<string, BundleReportBundleData> bundles, string name)
        {
            if (string.IsNullOrEmpty(name) || !bundles.ContainsKey(name))
                throw new InvalidDataException("报告引用了不存在的资源包：" + name);
        }

        private static void ValidateAsset(string path, string guid)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                (!string.IsNullOrEmpty(guid) && (guid.Length != 32 || !guid.All(Uri.IsHexDigit))))
                throw new InvalidDataException("报告资源缺少路径或包含无效 GUID。");
        }
    }
}
