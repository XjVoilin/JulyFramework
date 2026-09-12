using System;
using System.Collections.Generic;
using LitJson;

namespace July.Release
{
    /// <summary>构建生成、启动读取的热更程序集清单协议；项目不手动维护第二份名单。</summary>
    public static class HybridClrManifest
    {
        public const string AssetAddress = "hybridclr-manifest";
        public const string FileName = AssetAddress + ".json";
        public const int FormatVersion = 1;

        /// <summary>在资源文件边界验证清单，保留构建确定的依赖顺序。</summary>
        public static string[] ReadHotUpdateAssemblies(string json, string registrarAssembly)
        {
            JsonData root;
            try { root = JsonMapper.ToObject(json); }
            catch (JsonException error)
            {
                throw new InvalidOperationException("热更程序集清单不是有效的 JSON，请重新生成并发布资源。", error);
            }
            if (root == null || !root.IsObject || !root.ContainsKey("formatVersion") ||
                root["formatVersion"] == null || !root["formatVersion"].IsInt || (int)root["formatVersion"] != FormatVersion)
                throw new InvalidOperationException("热更程序集清单版本不支持，请通过当前 Release 构建流程重新生成并发布资源。");
            if (!root.ContainsKey("hotUpdateAssemblies") || root["hotUpdateAssemblies"] == null ||
                !root["hotUpdateAssemblies"].IsArray || root["hotUpdateAssemblies"].Count == 0)
                throw new InvalidOperationException("热更程序集清单缺少有效的 hotUpdateAssemblies 数组。");

            var entries = root["hotUpdateAssemblies"];
            var names = new string[entries.Count];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null || !entries[i].IsString)
                    throw new InvalidOperationException("热更程序集清单中的名称必须是字符串。");
                var name = (string)entries[i];
                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
                    name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !seen.Add(name))
                    throw new InvalidOperationException($"热更程序集名称无效或重复：{name}。请使用不含路径和 .dll 后缀的简单名称。");
                names[i] = name;
            }
            if (!seen.Contains(registrarAssembly))
                throw new InvalidOperationException($"热更清单未包含注册入口程序集 {registrarAssembly}，请检查 HybridCLR Settings 后重新构建。");
            return names;
        }
    }
}
