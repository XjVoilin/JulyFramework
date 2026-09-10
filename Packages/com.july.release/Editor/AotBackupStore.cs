using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LitJson;

namespace July.Release.Editor
{
    public sealed class AotBackupManifest
    {
        public int formatVersion;
        public string platform;
        public string buildTarget;
        public string coreVersion;
        public string[] requiredAotAssemblies;
        public AotBackupFile[] files;
    }

    public sealed class AotBackupFile
    {
        public string path;
        public long size;
        public string sha256;
    }

    public sealed class AotBackupSnapshot
    {
        public AotBackupManifest Manifest { get; }
        public string ManifestSha256 { get; }
        internal string Json { get; }
        internal AotBackupSnapshot(AotBackupManifest manifest, string json)
        {
            Manifest = manifest;
            Json = json;
            ManifestSha256 = AotBackupStore.Hash(Encoding.UTF8.GetBytes(json));
        }
    }

    /// <summary>持久 AOT 备份的格式、完整性与事务式文件操作。不识别 Jenkins 或其目录布局。</summary>
    public static class AotBackupStore
    {
        public const string ManifestName = "aot-backup.json";
        const string ReferencesName = "AOTGenericReferences.cs";
        const string SourceHashName = "aot-source.hash";
        const int FormatVersion = 1;
        static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public static string AbsolutePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) ||
                value.StartsWith(@"\\?\", StringComparison.Ordinal) || value.StartsWith(@"\\.\", StringComparison.Ordinal))
                throw new ArgumentException($"AOT 备份路径必须是绝对路径: {value}");
            var root = Path.GetPathRoot(value);
            foreach (var part in value.Substring(root.Length).Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "." || part == "..") continue;
                CheckSegment(part);
            }
            var full = Path.GetFullPath(value);
            if (full.Length > Path.GetPathRoot(full).Length) full = full.TrimEnd('/', '\\');
            RejectLinkedAncestors(full);
            return full;
        }

        static void CheckSegment(string part)
        {
            if (Regex.IsMatch(part, "[<>:\"|?*\\x00-\\x1f]") || part.EndsWith(" ", StringComparison.Ordinal) || part.EndsWith(".", StringComparison.Ordinal) ||
                Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                throw new ArgumentException($"AOT 路径包含非法目录或文件名: {part}");
        }

        static void RejectLinkedAncestors(string path)
        {
            for (var current = path; current != null; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"AOT 备份路径不允许符号链接或目录联接: {current}");
        }

        public static void RequireDisjoint(string persistentDirectory, string workspaceDirectory)
        {
            var persistent = AbsolutePath(persistentDirectory);
            var workspace = AbsolutePath(Path.GetFullPath(workspaceDirectory));
            if (Within(persistent, workspace) || Within(workspace, persistent))
                throw new IOException($"持久 AOT 目录与工作备份目录不能相同或互为父子目录: {persistent} / {workspace}");
        }

        static bool Within(string path, string root) => string.Equals(path, root, PathComparison) ||
            path.StartsWith(root.TrimEnd('/', '\\') + Path.DirectorySeparatorChar, PathComparison);

        public static void CheckOutput(string workspaceDirectory, string outputDirectory)
        {
            RequireDisjoint(outputDirectory, workspaceDirectory);
            var output = AbsolutePath(outputDirectory);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException($"AOT 输出目录已存在，拒绝覆盖: {output}");
        }

        public static AotBackupSnapshot Read(string directory, string platform, string buildTarget, string expectedCoreVersion = null)
        {
            directory = AbsolutePath(directory);
            var manifestPath = Path.Combine(directory, ManifestName);
            if (!File.Exists(manifestPath)) throw new InvalidDataException($"AOT 备份缺失或未完成（缺少 {ManifestName}）: {directory}");
            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            AotBackupManifest manifest;
            try { manifest = JsonMapper.ToObject<AotBackupManifest>(json); }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is InvalidCastException || exception is FormatException || exception is OverflowException)
            { throw new InvalidDataException($"AOT 清单格式损坏: {manifestPath}", exception); }
            if (manifest == null || manifest.formatVersion != FormatVersion)
                throw new InvalidDataException($"不支持的 AOT 备份格式: {manifestPath}");
            if (manifest.platform != platform || manifest.buildTarget != buildTarget)
                throw new InvalidDataException($"AOT 平台不匹配，要求 {platform}/{buildTarget}，备份为 {manifest.platform}/{manifest.buildTarget}");
            if (!BuildUtils.IsValidVersion(manifest.coreVersion) ||
                (expectedCoreVersion != null && manifest.coreVersion != expectedCoreVersion))
                throw new InvalidDataException($"AOT CoreVersion 不匹配或非法，要求 {expectedCoreVersion ?? "有效版本"}，备份为 {manifest.coreVersion}");
            if (manifest.files == null || manifest.files.Length == 0 || manifest.requiredAotAssemblies == null || manifest.requiredAotAssemblies.Length == 0)
                throw new InvalidDataException("AOT 清单缺少文件或必需程序集列表。");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.files)
            {
                if (file == null) throw new InvalidDataException("AOT 清单包含空文件项。");
                CheckRelativeFile(file.path);
                if (string.Equals(file.path, ManifestName, StringComparison.OrdinalIgnoreCase) || !paths.Add(file.path) || file.size < 0 || !IsHash(file.sha256))
                    throw new InvalidDataException($"非法或重复的 AOT 文件清单项: {file.path}");
            }
            foreach (var name in manifest.requiredAotAssemblies.Concat(new[] { ReferencesName, SourceHashName }))
                if (string.IsNullOrWhiteSpace(name) || !paths.Contains(name)) throw new InvalidDataException($"AOT 清单缺少必需文件: {name}");
            return new AotBackupSnapshot(manifest, json);
        }

        public static void SelectInput(BuildContext context)
        {
            var snapshot = Read(context.AotBackupInputPath, context.Platform, context.Target.ToString(), context.AOTBackupVersion);
            context.ExplicitAotBackup = snapshot;
            context.CoreVersion = snapshot.Manifest.coreVersion;
            context.AOTBackupVersion = snapshot.Manifest.coreVersion;
            context.ExplicitAotBackupRestored = false;
        }

        public static void ValidateInput(BuildContext context, string workspaceDirectory, IReadOnlyList<string> mandatoryAssemblies)
        {
            RequireDisjoint(context.AotBackupInputPath, workspaceDirectory);
            Validate(context.AotBackupInputPath, context.ExplicitAotBackup, context.Platform, context.Target.ToString(), context.CoreVersion, mandatoryAssemblies);
        }

        public static void Validate(string directory, AotBackupSnapshot selected, string platform, string buildTarget, string coreVersion, IReadOnlyList<string> mandatoryAssemblies)
        {
            var current = Read(directory, platform, buildTarget, coreVersion);
            if (current.ManifestSha256 != selected.ManifestSha256)
                throw new InvalidDataException($"选中的 AOT 清单已发生变化: {directory}");
            VerifyPayload(directory, selected.Manifest, mandatoryAssemblies);
        }

        public static AotBackupSnapshot Save(string workspaceDirectory, string outputDirectory, string platform, string buildTarget, string coreVersion, IReadOnlyList<string> mandatoryAssemblies)
        {
            CheckOutput(workspaceDirectory, outputDirectory);
            var output = AbsolutePath(outputDirectory);
            var source = AbsolutePath(Path.GetFullPath(workspaceDirectory));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var temporary = output + ".aot-tmp-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(temporary);
            try
            {
                var manifest = new AotBackupManifest
                {
                    formatVersion = FormatVersion, platform = platform, buildTarget = buildTarget, coreVersion = coreVersion,
                    requiredAotAssemblies = RequiredAssemblies(source, mandatoryAssemblies),
                    files = Inventory(source).Where(path => path != ManifestName).Select(path => new AotBackupFile
                    {
                        path = path, size = new FileInfo(Path.Combine(source, path)).Length, sha256 = HashFile(Path.Combine(source, path))
                    }).OrderBy(file => file.path, StringComparer.Ordinal).ToArray()
                };
                foreach (var file in manifest.files) CopyFile(source, temporary, file.path);
                VerifyPayload(temporary, manifest, mandatoryAssemblies);
                var snapshot = new AotBackupSnapshot(manifest, JsonMapper.ToJson(manifest));
                // 清单最后写入；最终目录只通过同父目录 rename 发布，不覆盖已有目录。
                File.WriteAllText(Path.Combine(temporary, ManifestName), snapshot.Json, new UTF8Encoding(false));
                Read(temporary, platform, buildTarget, coreVersion);
                Directory.Move(temporary, output);
                return snapshot;
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }

        public static AotBackupSnapshot Archive(BuildContext context, string workspaceDirectory, string localArchiveDirectory, IReadOnlyList<string> mandatoryAssemblies)
        {
            if (context.AotBackupOutputPath != null)
                return Save(workspaceDirectory, context.AotBackupOutputPath, context.Platform, context.Target.ToString(), context.CoreVersion, mandatoryAssemblies);
            BuildUtils.CopyDirectory(workspaceDirectory, localArchiveDirectory);
            return null;
        }

        public static void Restore(BuildContext context, string workspaceDirectory, string localArchiveDirectory, IReadOnlyList<string> mandatoryAssemblies)
        {
            if (context.AotBackupInputPath == null)
            {
                if (Directory.Exists(workspaceDirectory)) return;
                if (!Directory.Exists(localArchiveDirectory)) throw new DirectoryNotFoundException($"AOT 工作副本及本地归档均不存在: {workspaceDirectory} / {localArchiveDirectory}");
                BuildUtils.CopyDirectory(localArchiveDirectory, workspaceDirectory);
                return;
            }
            context.ExplicitAotBackupRestored = false;
            RequireDisjoint(context.AotBackupInputPath, workspaceDirectory);
            var workspace = AbsolutePath(Path.GetFullPath(workspaceDirectory));
            var snapshot = context.ExplicitAotBackup;
            var current = Read(context.AotBackupInputPath, context.Platform, context.Target.ToString(), context.CoreVersion);
            if (current.ManifestSha256 != snapshot.ManifestSha256) throw new InvalidDataException("恢复前 AOT 清单已改变。");
            if (!new HashSet<string>(snapshot.Manifest.files.Select(file => file.path), StringComparer.Ordinal)
                    .SetEquals(Inventory(context.AotBackupInputPath).Where(path => path != ManifestName)))
                throw new InvalidDataException("恢复前 AOT 文件清单已改变。");
            Directory.CreateDirectory(Path.GetDirectoryName(workspace));
            var temporary = workspace + ".aot-tmp-" + Guid.NewGuid().ToString("N");
            var previous = workspace + ".aot-previous-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(temporary);
            try
            {
                // 不按当前源目录重新枚举；只复制已选择清单的内容，再完整校验工作副本。
                foreach (var file in snapshot.Manifest.files) CopyFile(context.AotBackupInputPath, temporary, file.path);
                VerifyPayload(temporary, snapshot.Manifest, mandatoryAssemblies);
                File.WriteAllText(Path.Combine(temporary, ManifestName), snapshot.Json, new UTF8Encoding(false));
                if (Directory.Exists(workspace)) Directory.Move(workspace, previous);
                try { Directory.Move(temporary, workspace); }
                catch
                {
                    // 文件系统事务失败时恢复旧工作副本；不修改持久备份源。
                    if (Directory.Exists(previous)) Directory.Move(previous, workspace);
                    throw;
                }
                context.ExplicitAotBackupRestored = true;
                if (Directory.Exists(previous)) Directory.Delete(previous, true);
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }

        public static void ValidateRestored(BuildContext context, string workspaceDirectory, IReadOnlyList<string> mandatoryAssemblies)
        {
            if (!context.ExplicitAotBackupRestored || context.AOTBackupVersion != context.CoreVersion)
                throw new InvalidDataException("指定 AOT 备份尚未恢复或热更基线发生变化，禁止继续编译。");
            Validate(workspaceDirectory, context.ExplicitAotBackup, context.Platform, context.Target.ToString(), context.CoreVersion, mandatoryAssemblies);
        }

        static void VerifyPayload(string directory, AotBackupManifest manifest, IReadOnlyList<string> mandatoryAssemblies)
        {
            var expected = new HashSet<string>(manifest.files.Select(file => file.path), StringComparer.Ordinal);
            var actual = Inventory(directory).Where(path => path != ManifestName);
            if (!expected.SetEquals(actual)) throw new InvalidDataException($"AOT 文件清单不一致（文件缺失或有额外文件）: {directory}");
            foreach (var file in manifest.files)
            {
                var path = Path.Combine(directory, file.path);
                if (new FileInfo(path).Length != file.size || HashFile(path) != file.sha256)
                    throw new InvalidDataException($"AOT 文件完整性校验失败: {path}");
            }
            var sourceHash = File.ReadAllText(Path.Combine(directory, SourceHashName)).Trim();
            if (!IsHash(sourceHash)) throw new InvalidDataException("AOT 备份缺少有效的源码 hash。");
            var required = RequiredAssemblies(directory, mandatoryAssemblies);
            foreach (var name in required.Concat(manifest.requiredAotAssemblies))
            {
                CheckRelativeFile(name);
                if (!manifest.requiredAotAssemblies.Contains(name, StringComparer.Ordinal) || !expected.Contains(name) || new FileInfo(Path.Combine(directory, name)).Length == 0)
                    throw new InvalidDataException($"AOT 备份缺少必需 DLL: {name}");
            }
        }

        static string[] RequiredAssemblies(string directory, IReadOnlyList<string> mandatoryAssemblies)
        {
            var path = Path.Combine(directory, ReferencesName);
            if (!File.Exists(path)) throw new InvalidDataException($"AOT 备份缺少 {ReferencesName}");
            var section = Regex.Match(File.ReadAllText(path), @"//\s*\{\{\s*AOT assemblies(.+?)//\s*\}\}", RegexOptions.Singleline);
            var parsed = Regex.Matches(section.Groups[1].Value, "\"([^\"]+\\.dll)\"").Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
            if (!section.Success || parsed.Length == 0) throw new InvalidDataException("AOT 泛型引用文件缺少程序集清单，禁止回退到当前 SDK 配置。");
            return parsed.Concat(mandatoryAssemblies.Select(name => name.EndsWith(".dll", StringComparison.Ordinal) ? name : name + ".dll"))
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        static IEnumerable<string> Inventory(string root)
        {
            RejectLinkedAncestors(Path.GetFullPath(root));
            return Enumerate(root, "").ToArray();
        }

        static IEnumerable<string> Enumerate(string directory, string prefix)
        {
            foreach (var entry in Directory.GetFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"AOT 备份不能包含链接: {entry}");
                var relative = prefix + Path.GetFileName(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                    foreach (var child in Enumerate(entry, relative + "/")) yield return child;
                else { CheckRelativeFile(relative); yield return relative; }
            }
        }

        static void CheckRelativeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\')) throw new InvalidDataException($"非法 AOT 相对文件路径: {path}");
            foreach (var part in path.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == "..") throw new InvalidDataException($"非法 AOT 相对文件路径: {path}");
                CheckSegment(part);
            }
        }

        static void CopyFile(string source, string destination, string relative)
        {
            var from = Path.Combine(source, relative);
            RejectLinkedAncestors(from);
            var to = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, false);
        }

        static bool IsHash(string value) => value != null && Regex.IsMatch(value, "^[0-9a-f]{64}$");
        internal static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return Hex(sha.ComputeHash(bytes)); }
        static string HashFile(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return Hex(sha.ComputeHash(stream)); }
        static string Hex(byte[] bytes) => string.Concat(bytes.Select(value => value.ToString("x2")));
    }
}
