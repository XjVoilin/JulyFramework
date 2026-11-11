using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Config.Authoring.Samples
{
    /// <summary>Opt-in project policy example; the framework itself has no Common or MiniGames rules.</summary>
    public sealed class BoxGenerationScheme : IConfigGenerationScheme
    {
        public IReadOnlyList<ConfigGenerationTask> CreateTasks(ConfigGenerationProject project)
        {
            var tasks = new List<ConfigGenerationTask> { Create(project, "Common") };
            var datas = Path.Combine(project.InputRoot, "Datas");
            foreach (var directory in Directory.GetDirectories(datas).OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(directory);
                if (name != "Common" && File.Exists(Path.Combine(directory, "__tables__.xlsx"))) tasks.Add(Create(project, name));
            }
            return tasks;
        }
        private static ConfigGenerationTask Create(ConfigGenerationProject project, string module)
        {
            var common = module == "Common";
            var schemas = new List<ConfigSchemaFile>();
            if (common) schemas.Add(new ConfigSchemaFile("Defines"));
            schemas.Add(new ConfigSchemaFile("Datas/" + module + "/__tables__.xlsx", "table"));
            foreach (var name in new[] { "__beans__.xlsx", "__enums__.xlsx" })
                if (File.Exists(Path.Combine(project.InputRoot, "Datas", module, name)))
                    schemas.Add(new ConfigSchemaFile("Datas/" + module + "/" + name, name == "__beans__.xlsx" ? "bean" : "enum"));
            var processors = new List<IConfigOutputProcessor> { new TablesExtensionProcessor() };
            if (!common) processors.Add(new BoxProviderProcessor());
            return new ConfigGenerationTask(module, common ? "公共配置" : module, common ? "公共配置" : "小游戏",
                common ? "cfg" : "cfg." + module,
                common ? "Assets/Game/Scripts/Generated" : "Assets/Game/MiniGames/" + module + "/Scripts/Generated",
                common ? "Assets/Game/Res/Configs" : "Assets/Game/MiniGames/" + module + "/Res/Configs",
                dataDirectory: "Datas/" + module, schemas: schemas, processors: processors);
        }

    }
}
