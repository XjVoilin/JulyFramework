using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace July.Build
{
    /// <summary>Creates a deterministic fingerprint for an AOT source tree and its defines.</summary>
    public static class AotSourceHasher
    {
        public const string HashFileName = "aot-source.hash";

        public static string ComputeHash(string sourceDirectory, BuildTargetGroup targetGroup,
            params string[] excludedDirectories)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException("Source directory cannot be empty.",
                    nameof(sourceDirectory));

            var rootFull = Path.GetFullPath(sourceDirectory);
            if (!Directory.Exists(rootFull))
            {
                Debug.LogWarning($"[AotSourceHasher] Source directory does not exist: {rootFull}");
                return string.Empty;
            }

            var rootNormalized = Normalize(rootFull);
            var exclusions = (excludedDirectories ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => "/" + value.Trim('/', '\\') + "/")
                .ToArray();

            var files = Directory.GetFiles(rootFull, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
                .Select(Normalize)
                .Where(path => exclusions.All(exclusion =>
                    path.IndexOf(exclusion, StringComparison.OrdinalIgnoreCase) < 0))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            using var aggregate = new MemoryStream();
            using var sha = SHA256.Create();
            foreach (var file in files)
            {
                var relativePath = file.Substring(rootNormalized.Length).TrimStart('/');
                Write(aggregate, Encoding.UTF8.GetBytes(relativePath + "\0"));
                using var stream = File.OpenRead(file);
                Write(aggregate, sha.ComputeHash(stream));
                aggregate.WriteByte((byte)'\n');
            }

            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup)
                .Split(';')
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(value => value, StringComparer.Ordinal);
            Write(aggregate, Encoding.UTF8.GetBytes(
                "__DEFINES__\0" + string.Join(";", defines) + "\n"));

            aggregate.Position = 0;
            return ToHex(sha.ComputeHash(aggregate));
        }

        public static string GetHashFilePath(string backupDirectory) =>
            Path.Combine(backupDirectory, HashFileName);

        public static void WriteHash(string backupDirectory, string hash)
        {
            var path = GetHashFilePath(backupDirectory);
            Directory.CreateDirectory(backupDirectory);
            File.WriteAllText(path, hash ?? string.Empty);
        }

        public static string ReadHash(string backupDirectory)
        {
            var path = GetHashFilePath(backupDirectory);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }

        private static string Normalize(string path) => path.Replace('\\', '/');

        private static void Write(Stream stream, byte[] bytes) =>
            stream.Write(bytes, 0, bytes.Length);

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
