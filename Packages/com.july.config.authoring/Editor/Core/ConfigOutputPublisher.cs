using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LitJson;

namespace July.Config.Authoring
{
    internal static class ConfigOutputPublisher
    {
        internal static void Publish(string root, IReadOnlyList<ConfigStagedOutput> outputs)
        {
            var manifestPath = ConfigPaths.Child(root, "ProjectSettings/July/ConfigAuthoring/outputs.json");
            var manifest = File.Exists(manifestPath)
                ? JsonMapper.ToObject<ConfigOutputManifest>(File.ReadAllText(manifestPath)) : new ConfigOutputManifest();
            var snapshots = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var newPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var writes = new List<(string Destination, byte[] Content)>();
            var stale = new List<string>();
            foreach (var output in outputs)
            {
                var previous = manifest.Tasks.SingleOrDefault(t => t.Id == output.Task.Id) ?? Adopt(root, output.Task);
                var next = new ConfigOwnedOutput { Id = output.Task.Id,
                    CodeRoot = ConfigPaths.Relative(root, Path.GetFullPath(Path.Combine(root, output.Task.CodeOutput))),
                    DataRoot = ConfigPaths.Relative(root, Path.GetFullPath(Path.Combine(root, output.Task.DataOutput))) };
                Collect(root, output.Code, next.CodeRoot, previous.CodeRoot, previous.CodeFiles, next.CodeFiles, writes, newPaths);
                Collect(root, output.Data, next.DataRoot, previous.DataRoot, previous.DataFiles, next.DataFiles, writes, newPaths);
                foreach (var file in OwnedPaths(root, previous)) stale.Add(file);
                manifest.Tasks.RemoveAll(t => t.Id == output.Task.Id);
                manifest.Tasks.Add(next);
            }
            try
            {
                foreach (var write in writes) Save(write.Destination, write.Content, snapshots);
                foreach (var path in stale.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (newPaths.Contains(path)) continue;
                    Capture(path, snapshots); Capture(path + ".meta", snapshots);
                    File.Delete(path); File.Delete(path + ".meta");
                }
                Save(manifestPath, Encoding.UTF8.GetBytes(JsonMapper.ToJson(manifest) + "\n"), snapshots);
            }
            catch (Exception publishError)
            {
                try
                {
                    foreach (var snapshot in snapshots)
                    {
                        if (snapshot.Value == null) File.Delete(snapshot.Key);
                        else if (!File.Exists(snapshot.Key) || !File.ReadAllBytes(snapshot.Key).SequenceEqual(snapshot.Value))
                        { Directory.CreateDirectory(Path.GetDirectoryName(snapshot.Key)); File.WriteAllBytes(snapshot.Key, snapshot.Value); }
                    }
                }
                catch (Exception rollbackError)
                { throw new AggregateException("Configuration publication and rollback failed.", publishError, rollbackError); }
                throw;
            }
        }

        private static void Collect(string root, string stage, string destinationRoot, string previousRoot,
            IReadOnlyList<string> previousFiles, List<string> nextFiles,
            List<(string Destination, byte[] Content)> writes, HashSet<string> newPaths)
        {
            var folder = AssetDirectory(root, destinationRoot);
            var oldFolder = AssetDirectory(root, previousRoot);
            var previous = new HashSet<string>(previousFiles.Select(p => ConfigPaths.Child(oldFolder, p)), StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
            {
                var relative = ConfigPaths.Relative(stage, file);
                if (relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Processors must not emit Unity metadata: " + relative);
                var destination = ConfigPaths.Child(folder, relative);
                if (Directory.Exists(destination)) throw new InvalidOperationException("A directory occupies generated file path: " + destination);
                if (!newPaths.Add(destination)) throw new InvalidOperationException("Duplicate generated file: " + destination);
                if (File.Exists(destination) && !previous.Contains(destination))
                    throw new InvalidOperationException("Refusing to replace an unowned file: " + destination);
                nextFiles.Add(relative);
                writes.Add((destination, File.ReadAllBytes(file)));
                var previousPath = ConfigPaths.Child(oldFolder, relative);
                if (!File.Exists(destination + ".meta") && previous.Contains(previousPath) && File.Exists(previousPath + ".meta"))
                    writes.Add((destination + ".meta", File.ReadAllBytes(previousPath + ".meta")));
            }
        }

        private static ConfigOwnedOutput Adopt(string root, ConfigGenerationTask task)
        {
            var entry = new ConfigOwnedOutput { Id = task.Id, CodeRoot = task.CodeOutput, DataRoot = task.DataOutput };
            var code = AssetDirectory(root, entry.CodeRoot);
            var data = AssetDirectory(root, entry.DataRoot);
            if (Directory.Exists(code))
            {
                foreach (var path in Directory.GetFiles(code, "*.cs", SearchOption.AllDirectories))
                {
                    using var reader = File.OpenText(path);
                    var header = new char[2048]; var count = reader.Read(header, 0, header.Length);
                    if (new string(header, 0, count).IndexOf("auto-generated", StringComparison.OrdinalIgnoreCase) >= 0)
                        entry.CodeFiles.Add(ConfigPaths.Relative(code, path));
                }
                if (entry.CodeFiles.Contains("Tables.cs"))
                    foreach (var table in TablesExtensionProcessor.ReadTables(code))
                        if (File.Exists(ConfigPaths.Child(data, table.LoadKey + ".json"))) entry.DataFiles.Add(table.LoadKey + ".json");
            }
            return entry;
        }

        private static IEnumerable<string> OwnedPaths(string root, ConfigOwnedOutput output)
        {
            foreach (var file in output.CodeFiles) yield return ConfigPaths.Child(AssetDirectory(root, output.CodeRoot), file);
            foreach (var file in output.DataFiles) yield return ConfigPaths.Child(AssetDirectory(root, output.DataRoot), file);
        }
        private static string AssetDirectory(string root, string relative)
        {
            var full = Path.GetFullPath(Path.Combine(root, relative));
            var assets = Path.Combine(root, "Assets");
            return ConfigPaths.Child(assets, ConfigPaths.Relative(assets, full));
        }
        private static void Capture(string path, Dictionary<string, byte[]> snapshots)
        {
            if (!snapshots.ContainsKey(path)) snapshots.Add(path, File.Exists(path) ? File.ReadAllBytes(path) : null);
        }
        private static void Save(string path, byte[] bytes, Dictionary<string, byte[]> snapshots)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes)) return;
            Capture(path, snapshots);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
        }
    }

    public sealed class ConfigOutputManifest
    {
        public List<ConfigOwnedOutput> Tasks = new List<ConfigOwnedOutput>();
    }
    public sealed class ConfigOwnedOutput
    {
        public string Id, CodeRoot, DataRoot;
        public List<string> CodeFiles = new List<string>();
        public List<string> DataFiles = new List<string>();
    }
}
