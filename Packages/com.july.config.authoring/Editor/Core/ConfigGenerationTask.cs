using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace July.Config.Authoring
{
    public interface IConfigGenerationScheme
    {
        IReadOnlyList<ConfigGenerationTask> CreateTasks(ConfigGenerationProject project);
    }

    public interface IConfigOutputProcessor
    {
        void Process(ConfigOutputContext context);
    }

    public sealed class ConfigGenerationProject
    {
        public string Root { get; }
        public string InputRoot => Path.Combine(Root, "Tools/Luban/DataTables");
        public ConfigGenerationProject(string root) => Root = Path.GetFullPath(root);
    }

    public sealed class ConfigSchemaFile
    {
        public string Path { get; }
        public string Type { get; }
        public ConfigSchemaFile(string path, string type = "") { Path = path; Type = type; }
    }

    public sealed class ConfigGenerationTask
    {
        public string Id { get; }
        public string Name { get; }
        public string Group { get; }
        public string Namespace { get; }
        public string ConfigFile { get; }
        public string DataDirectory { get; }
        public IReadOnlyList<ConfigSchemaFile> Schemas { get; }
        public string CodeOutput { get; set; }
        public string DataOutput { get; set; }
        public IReadOnlyList<IConfigOutputProcessor> Processors { get; }

        public ConfigGenerationTask(string id, string name, string group, string @namespace,
            string codeOutput, string dataOutput, string configFile = null,
            string dataDirectory = null, IEnumerable<ConfigSchemaFile> schemas = null,
            IEnumerable<IConfigOutputProcessor> processors = null)
        {
            Id = id; Name = name; Group = group; Namespace = @namespace;
            CodeOutput = codeOutput; DataOutput = dataOutput;
            ConfigFile = configFile; DataDirectory = dataDirectory;
            Schemas = (schemas ?? Array.Empty<ConfigSchemaFile>()).ToArray();
            Processors = (processors ?? new IConfigOutputProcessor[] { new TablesExtensionProcessor() }).ToArray();
        }
    }

    public sealed class ConfigTableInfo
    {
        public string TypeName { get; }
        public string PropertyName { get; }
        public string LoadKey { get; }
        public ConfigTableInfo(string typeName, string propertyName, string loadKey)
        { TypeName = typeName; PropertyName = propertyName; LoadKey = loadKey; }
    }

    public sealed class ConfigOutputContext
    {
        public ConfigGenerationTask Task { get; }
        public string CodeDirectory { get; }
        public string DataDirectory { get; }
        public IReadOnlyList<ConfigTableInfo> Tables { get; }
        internal ConfigOutputContext(ConfigGenerationTask task, string code, string data,
            IReadOnlyList<ConfigTableInfo> tables)
        { Task = task; CodeDirectory = code; DataDirectory = data; Tables = tables; }

        public void WriteCode(string relativePath, string text)
        {
            var path = ConfigPaths.Child(CodeDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text.Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
        }
    }

    internal static class ConfigPaths
    {
        public static string Child(string root, string path)
        {
            var parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(parent, path));
            if (!full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path must stay inside " + parent + ": " + path);
            return full;
        }
        public static string Relative(string root, string path)
            => Path.GetRelativePath(root, path).Replace('\\', '/');
        public static bool Overlaps(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
               a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
