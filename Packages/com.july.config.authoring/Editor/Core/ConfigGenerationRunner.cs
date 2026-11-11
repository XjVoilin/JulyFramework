using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LitJson;

namespace July.Config.Authoring
{
    public sealed class ConfigGenerationResult
    {
        public bool Succeeded { get; }
        public string Error { get; }
        public int TaskCount { get; }
        internal ConfigGenerationResult(bool succeeded, string error, int count)
        { Succeeded = succeeded; Error = error; TaskCount = count; }
    }

    public sealed class ConfigGenerationRunner
    {
        private readonly ConfigGenerationProject _project;
        private readonly IConfigProcessRunner _process;
        public ConfigGenerationRunner(string projectRoot, IConfigProcessRunner process = null)
        { _project = new ConfigGenerationProject(projectRoot); _process = process ?? new ConfigProcessRunner(); }

        public ConfigGenerationResult Run(IReadOnlyList<ConfigGenerationTask> tasks, Action<float, string> progress = null)
        {
            var cache = ConfigPaths.Child(_project.Root, "Library/July/ConfigAuthoring");
            var stage = ConfigPaths.Child(cache, Guid.NewGuid().ToString("N"));
            try
            {
                ValidateTasks(_project.Root, tasks);
                var dll = ConfigPaths.Child(_project.Root, "Tools/Luban/Luban/Luban.dll");
                if (!File.Exists(dll)) throw new FileNotFoundException("Missing project-owned Luban tool.", dll);
                var dotnet = ConfigProcessRunner.ResolveDotnet();
                _process.Run(dotnet, "--version", _project.Root);
                Directory.CreateDirectory(stage);
                var generated = new List<ConfigStagedOutput>();
                for (var index = 0; index < tasks.Count; index++)
                {
                    var task = tasks[index];
                    progress?.Invoke((float)index / tasks.Count, task.Name);
                    var folder = ConfigPaths.Child(stage, index.ToString());
                    var code = ConfigPaths.Child(folder, "Code");
                    var data = ConfigPaths.Child(folder, "Data");
                    Directory.CreateDirectory(code); Directory.CreateDirectory(data);
                    var conf = ResolveConfiguration(task, folder);
                    _process.Run(dotnet,
                        ConfigProcessRunner.Quote(dll) + " -t all -d json -c cs-simple-json --conf " +
                        ConfigProcessRunner.Quote(conf) + " -x outputCodeDir=" + ConfigProcessRunner.Quote(code) +
                        " -x outputDataDir=" + ConfigProcessRunner.Quote(data), _project.InputRoot);
                    var context = new ConfigOutputContext(task, code, data, TablesExtensionProcessor.ReadTables(code));
                    foreach (var processor in task.Processors) processor.Process(context);
                    generated.Add(new ConfigStagedOutput(task, code, data));
                }
                ConfigOutputPublisher.Publish(_project.Root, generated);
                return new ConfigGenerationResult(true, null, tasks.Count);
            }
            catch (Exception error)
            {
                // External tool, project processor and filesystem failures share one observable result.
                return new ConfigGenerationResult(false, error.Message, 0);
            }
            finally
            {
                // stage was created exclusively below this package's cache root.
                if (Directory.Exists(stage) && ConfigPaths.Child(cache, Path.GetFileName(stage)) == stage)
                    Directory.Delete(stage, true);
            }
        }

        public static void ValidateTasks(string projectRoot, IReadOnlyList<ConfigGenerationTask> tasks)
        {
            if (tasks.Count == 0) throw new InvalidOperationException("No configuration tasks selected.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var outputs = new List<string>();
            foreach (var task in tasks)
            {
                if (string.IsNullOrWhiteSpace(task.Id) || !names.Add(task.Id))
                    throw new InvalidOperationException("Empty or duplicate generation task ID: " + task.Id);
                if (!Regex.IsMatch(task.Namespace ?? "", @"^[A-Za-z_]\w*(\.[A-Za-z_]\w*)*$"))
                    throw new InvalidOperationException("Invalid generated namespace for " + task.Id);
                foreach (var path in new[] { task.CodeOutput, task.DataOutput })
                {
                    if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Output directory is empty: " + task.Id);
                    var output = ConfigPaths.Child(Path.Combine(projectRoot, "Assets"),
                        ConfigPaths.Relative(Path.Combine(projectRoot, "Assets"), Path.GetFullPath(Path.Combine(projectRoot, path))));
                    if (outputs.Any(other => ConfigPaths.Overlaps(output, other)))
                        throw new InvalidOperationException("Configuration output directories overlap: " + path);
                    outputs.Add(output);
                }
            }
        }

        private string ResolveConfiguration(ConfigGenerationTask task, string folder)
        {
            if (!string.IsNullOrEmpty(task.ConfigFile))
            {
                var path = Path.GetFullPath(Path.Combine(_project.Root, task.ConfigFile));
                if (!File.Exists(path)) throw new FileNotFoundException("Configuration file not found.", path);
                return path;
            }
            if (task.Schemas.Count == 0 || string.IsNullOrEmpty(task.DataDirectory))
                throw new InvalidOperationException("Schema-based task needs schemas and a data directory: " + task.Id);
            var root = new JsonData(); root.SetJsonType(JsonType.Object);
            var groups = new JsonData(); groups.SetJsonType(JsonType.Array);
            foreach (var group in new[] { "c", "s", "e" })
            {
                var entry = new JsonData(); entry["names"] = new JsonData(); entry["names"].SetJsonType(JsonType.Array);
                entry["names"].Add(group); entry["default"] = true; groups.Add(entry);
            }
            root["groups"] = groups;
            root["dataDir"] = Path.GetFullPath(Path.Combine(_project.InputRoot, task.DataDirectory));
            root["schemaFiles"] = new JsonData(); root["schemaFiles"].SetJsonType(JsonType.Array);
            foreach (var file in task.Schemas)
            {
                var entry = new JsonData(); entry["fileName"] = Path.GetFullPath(Path.Combine(_project.InputRoot, file.Path));
                entry["type"] = file.Type; root["schemaFiles"].Add(entry);
            }
            var target = new JsonData(); target["name"] = "all"; target["manager"] = "Tables";
            target["topModule"] = task.Namespace;
            target["groups"] = new JsonData(); target["groups"].SetJsonType(JsonType.Array);
            foreach (var group in new[] { "c", "s", "e" }) target["groups"].Add(group);
            root["targets"] = new JsonData(); root["targets"].SetJsonType(JsonType.Array); root["targets"].Add(target);
            var conf = Path.Combine(folder, "luban.conf");
            File.WriteAllText(conf, root.ToJson());
            return conf;
        }
    }

    internal sealed class ConfigStagedOutput
    {
        internal readonly ConfigGenerationTask Task;
        internal readonly string Code, Data;
        internal ConfigStagedOutput(ConfigGenerationTask task, string code, string data) { Task = task; Code = code; Data = data; }
    }
}
