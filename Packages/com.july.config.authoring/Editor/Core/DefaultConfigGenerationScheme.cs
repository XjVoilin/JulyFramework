using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LitJson;

namespace July.Config.Authoring
{
    public sealed class DefaultConfigGenerationScheme : IConfigGenerationScheme
    {
        public IReadOnlyList<ConfigGenerationTask> CreateTasks(ConfigGenerationProject project)
        {
            var conf = Path.Combine(project.InputRoot, "luban.conf");
            if (!File.Exists(conf))
                throw new FileNotFoundException("Default scheme requires Tools/Luban/DataTables/luban.conf.", conf);
            var json = JsonMapper.ToObject(File.ReadAllText(conf));
            var target = ((System.Collections.IEnumerable)json["targets"]).Cast<JsonData>()
                .Single(t => (string)t["name"] == "all");
            if ((string)target["manager"] != "Tables")
                throw new InvalidOperationException("The default scheme requires manager=Tables.");
            return new[] { new ConfigGenerationTask("Default", "全部配置", "", (string)target["topModule"],
                "Assets/Game/Scripts/Runtime/Generated", "Assets/Game/Res/Configs", configFile: conf) };
        }
    }
}
