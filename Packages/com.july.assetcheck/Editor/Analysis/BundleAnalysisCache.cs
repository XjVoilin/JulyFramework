using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace July.AssetCheck
{
    [Serializable]
    internal sealed class BundleAnalysisViewState
    {
        public bool resources, descending = true, resourceDescending, showUnknown;
        public int kind, resourceSort;
        public string directory = "", search = "", filter = "全部", sort = "size", owner, package, resource, rule;
        public string[] expanded = Array.Empty<string>(), openDetails = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class BundleAnalysisCacheData
    {
        public int version;
        public bool hasSizes;
        public string reportPath, reportSignature, reportBytes, sizeJson, checkedAt;
        public AssetCheckSettings settings;
        public BundleCachedResource[] resources;
    }

    [Serializable]
    internal sealed class BundleAnalysisRecord
    {
        public int version, target;
        public string reportName, reportSignature, package, packageVersion, buildDate;
        internal string Id, DirectoryPath;
        internal string DataPath => Path.Combine(DirectoryPath, "inspection.json");
        internal string ViewPath => Path.Combine(DirectoryPath, "view.json");
        public override string ToString() => reportName + " | " + package + " / " + (UnityEditor.BuildTarget)target + " / " + packageVersion;
    }

    [Serializable]
    internal sealed class BundleCachedResource
    {
        public string id, checkedPath;
        public string[] notes, sameContent;
        public BundleCachedCheck[] checks;
    }

    [Serializable]
    internal sealed class BundleCachedCheck
    {
        public AssetCheckKind kind;
        public AssetCheckIssueSnapshot[] issues;
    }

    // One complete inspection per report identity. View state never rewrites historical inspection evidence.
    internal sealed class BundleAnalysisCache
    {
        [Serializable] sealed class Selection { public string id; }
        internal string DirectoryPath { get; }
        internal string Notice { get; private set; }
        internal BundleAnalysisCache(string projectDirectory) => DirectoryPath = Path.Combine(projectDirectory, "Library/July/AssetCheck/BundleReports");

        internal BundleAnalysisRecord[] Records()
        {
            Notice = null;
            if (!Directory.Exists(DirectoryPath)) return Array.Empty<BundleAnalysisRecord>();
            var records = new List<BundleAnalysisRecord>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var directory in Directory.GetDirectories(DirectoryPath).Where(path => !Path.GetFileName(path).StartsWith(".pending_", StringComparison.Ordinal)).OrderBy(path => path, StringComparer.Ordinal))
            {
                try
                {
                    var record = ReadJson<BundleAnalysisRecord>(Path.Combine(directory, "record.json"));
                    if (record == null || record.version != 1 || string.IsNullOrWhiteSpace(record.reportName) || string.IsNullOrWhiteSpace(record.package) ||
                        string.IsNullOrWhiteSpace(record.packageVersion) || string.IsNullOrWhiteSpace(record.buildDate) ||
                        record.reportSignature == null || record.reportSignature.Length != 64 || !record.reportSignature.All(Uri.IsHexDigit) ||
                        !Enum.IsDefined(typeof(UnityEditor.BuildTarget), record.target))
                        throw new InvalidDataException("历史报告元数据无效。");
                    record.DirectoryPath = directory; record.Id = Path.GetFileName(directory);
                    if (!File.Exists(record.DataPath)) throw new InvalidDataException("缺少完整检查快照。");
                    if (!identities.Add(record.reportSignature)) throw new InvalidDataException("重复的 report 身份，请检查复制的历史目录。");
                    records.Add(record);
                }
                catch (Exception exception) when (IsReadError(exception))
                { Notice = "部分历史记录无法读取：" + Path.GetFileName(directory) + "；" + exception.Message; }
            }
            return records.OrderByDescending(record => File.GetLastWriteTimeUtc(record.DataPath)).ThenBy(record => record.Id, StringComparer.Ordinal).ToArray();
        }

        internal BundleAnalysisRecord Save(BundleInspectionSession session, BundleAnalysisViewState view)
        {
            if (!session.Complete) throw new InvalidOperationException("仅可缓存完整的入包资源检查结果。");
            var report = session.Packages.Report;
            var record = Records().SingleOrDefault(item => item.reportSignature == report.Signature);
            if (session.IsSnapshot)
            {
                if (record == null) throw new InvalidDataException("历史记录不存在，不能用查看状态重建检查结果。");
                SaveView(record, view); return record;
            }
            var summary = report.Data.Summary;
            bool added = record == null;
            if (added)
            {
                var stem = Path.GetFileNameWithoutExtension(report.SourcePath);
                stem = new string(stem.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).Take(32).ToArray());
                var id = stem + "_" + report.Signature.Substring(0, 16).ToLowerInvariant();
                var directory = Path.Combine(DirectoryPath, id);
                if (Directory.Exists(directory)) throw new InvalidDataException("历史目录已存在但身份不匹配，请检查：" + directory);
                record = new BundleAnalysisRecord { version = 1, reportName = Path.GetFileName(report.SourcePath), reportSignature = report.Signature,
                    target = summary.BuildTarget, package = summary.BuildPackageName, packageVersion = summary.BuildPackageVersion,
                    buildDate = summary.BuildDate, Id = id, DirectoryPath = directory };
            }
            var data = new BundleAnalysisCacheData
            {
                version = 2, reportPath = report.SourcePath, reportSignature = report.Signature,
                reportBytes = Convert.ToBase64String(report.ReportBytes), hasSizes = report.SizeJson != null, sizeJson = report.SizeJson,
                checkedAt = session.CheckedAt.ToUniversalTime().ToString("O"), settings = session.Settings,
                resources = session.Resources.Select(resource => new BundleCachedResource
                {
                    id = BundlePackageAnalysis.Identity(resource.Asset), checkedPath = resource.CheckedPath,
                    notes = resource.Notes.ToArray(), sameContent = resource.SameContent,
                    checks = resource.Checks.Select(check => new BundleCachedCheck
                    { kind = check.Kind, issues = check.Issues.Select(AssetCheckIssueSnapshot.Capture).ToArray() }).ToArray()
                }).ToArray()
            };
            if (added)
            {
                var final = record.DirectoryPath;
                var staging = Path.Combine(DirectoryPath, ".pending_" + Guid.NewGuid().ToString("N"));
                record.DirectoryPath = staging;
                try
                {
                    Directory.CreateDirectory(staging);
                    WriteJson(record.DataPath, data); WriteJson(Path.Combine(staging, "record.json"), record); WriteJson(record.ViewPath, view);
                    Directory.Move(staging, final);
                }
                finally
                {
                    record.DirectoryPath = final;
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                }
                WriteJson(Path.Combine(DirectoryPath, "last.json"), new Selection { id = record.Id });
            }
            else { WriteJson(record.DataPath, data); SaveView(record, view); }
            return record;
        }

        internal void SaveView(BundleAnalysisRecord record, BundleAnalysisViewState view)
        {
            WriteJson(record.ViewPath, view);
            WriteJson(Path.Combine(DirectoryPath, "last.json"), new Selection { id = record.Id });
        }

        internal (BundleInspectionSession Session, BundleAnalysisViewState View) Load(string id = null)
        {
            var record = FindRecord(id);
            if (record == null) return (null, null);
            var session = ReadInspection(record);
            var view = ReadJson<BundleAnalysisViewState>(record.ViewPath);
            if (view == null || (view.kind != -1 && !Enum.IsDefined(typeof(AssetCheckKind), view.kind)) ||
                !Enum.IsDefined(typeof(AssetCheckSort), view.resourceSort) || view.expanded == null || view.openDetails == null)
                throw new InvalidDataException("AB 分析缓存查看状态无效。");
            view.directory = BundleResourceDirectoryFilter.Normalize(view.directory);
            return (session, view);
        }

        internal BundleInspectionSession LoadInspection(string id)
        {
            var record = FindRecord(id);
            if (record == null) throw new InvalidDataException("没有可比较的完整历史记录。");
            return ReadInspection(record);
        }

        BundleAnalysisRecord FindRecord(string id)
        {
            var records = Records();
            if (records.Length == 0) return null;
            if (id == null)
            {
                var last = Path.Combine(DirectoryPath, "last.json");
                if (File.Exists(last)) id = ReadJson<Selection>(last)?.id;
            }
            var record = id == null ? records[0] : records.SingleOrDefault(item => item.Id == id);
            if (record == null) throw new InvalidDataException("所选历史记录不存在，请重新选择。");
            return record;
        }

        static BundleInspectionSession ReadInspection(BundleAnalysisRecord record)
        {
            var data = ReadJson<BundleAnalysisCacheData>(record.DataPath);
            if (data == null || data.version != 2 || string.IsNullOrWhiteSpace(data.reportPath) || data.reportSignature != record.reportSignature ||
                data.reportBytes == null || data.settings == null || data.resources == null ||
                !DateTime.TryParseExact(data.checkedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var checkedAt))
                throw new InvalidDataException("AB 分析快照版本、身份、时间或内容无效。");
            data.settings.Validate();
            byte[] bytes;
            try { bytes = Convert.FromBase64String(data.reportBytes); }
            catch (FormatException exception) { throw new InvalidDataException("归档 report 字节无效。", exception); }
            if (data.hasSizes ? string.IsNullOrWhiteSpace(data.sizeJson) : !string.IsNullOrEmpty(data.sizeJson))
                throw new InvalidDataException("归档大小统计存在标记与内容不一致。");
            var report = BundleBuildReport.ReadStored(data.reportPath, bytes, data.hasSizes ? data.sizeJson : null);
            if (report.Signature != record.reportSignature) throw new InvalidDataException("归档 report 指纹不匹配。");
            var summary = report.Data.Summary;
            if (record.target != summary.BuildTarget || record.package != summary.BuildPackageName ||
                record.packageVersion != summary.BuildPackageVersion || record.buildDate != summary.BuildDate)
                throw new InvalidDataException("历史元数据与归档 report 不一致。");
            var session = new BundleInspectionSession(report, data.settings, checkedAt.ToLocalTime());
            var resources = session.Resources.ToDictionary(resource => BundlePackageAnalysis.Identity(resource.Asset), StringComparer.Ordinal);
            if (data.resources.Length != resources.Count || data.resources.Any(resource => resource == null) ||
                data.resources.Select(resource => resource.id).Distinct(StringComparer.Ordinal).Count() != resources.Count)
                throw new InvalidDataException("AB 分析缓存资源集合无效。");
            foreach (var row in data.resources)
            {
                if (row.id == null || !resources.TryGetValue(row.id, out var resource) || row.checks == null || row.notes == null || row.sameContent == null ||
                    row.notes.Any(note => note == null) || row.sameContent.Any(path => path == null) ||
                    row.checks.Any(check => check == null || !Enum.IsDefined(typeof(AssetCheckKind), check.kind) || check.issues == null) ||
                    row.checks.Select(check => check.kind).Distinct().Count() != row.checks.Length)
                    throw new InvalidDataException("AB 分析缓存资源检查记录无效。");
                resource.CheckedPath = row.checkedPath; resource.Checked = true; resource.ContentChecked = true;
                resource.Notes.AddRange(row.notes); resource.SameContent = row.sameContent;
                foreach (var check in row.checks)
                {
                    var restored = new BundleResourceCheck { Kind = check.kind, Complete = true };
                    foreach (var issue in check.issues)
                    {
                        if (issue == null) throw new InvalidDataException("AB 分析缓存问题行为空。");
                        restored.Issues.Add(issue.Restore(check.kind));
                    }
                    resource.Checks.Add(restored);
                }
            }
            var checkedPaths = new HashSet<string>(session.Resources.Where(resource => !string.IsNullOrEmpty(resource.CheckedPath))
                .Select(resource => resource.CheckedPath), StringComparer.Ordinal);
            if (session.Resources.Any(resource => resource.SameContent.Any(path => !checkedPaths.Contains(path))))
                throw new InvalidDataException("AB 分析缓存内容重复记录引用了不存在的检查资源。");
            return session;
        }

        static T ReadJson<T>(string path)
        {
            try { return JsonUtility.FromJson<T>(File.ReadAllText(path)); }
            catch (ArgumentException exception) { throw new InvalidDataException("历史记录 JSON 无法解析：" + path, exception); }
        }
        static void WriteJson<T>(string path, T data)
        {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        internal static bool IsReadError(Exception exception) => exception is InvalidDataException || exception is IOException ||
            exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException;
    }
}
