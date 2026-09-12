using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridCLR.Editor.Meta;
using LitJson;

namespace July.Release.Editor
{
    internal static class HybridClrAssemblyManifest
    {
        [Serializable]
        internal sealed class Manifest
        {
            public int formatVersion;
            public string[] hotUpdateAssemblies;
            public string[] aotMetadataAssemblies;
        }

        internal static void Write(string hotUpdateDirectory, IEnumerable<string> hotUpdateAssemblies,
            IEnumerable<string> aotMetadataAssemblies)
        {
            var hotNames = hotUpdateAssemblies.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (hotNames.Length == 0) throw new InvalidOperationException("没有生成的热更 DLL，无法写入程序集清单。");
            var ordered = AssemblySorter.SortAssemblyByReferenceOrder(hotNames, new CopiedAssemblyResolver(hotUpdateDirectory));
            var manifest = new Manifest { formatVersion = HybridClrManifest.FormatVersion, hotUpdateAssemblies = ordered.ToArray(),
                aotMetadataAssemblies = aotMetadataAssemblies.OrderBy(name => name, StringComparer.Ordinal).ToArray() };
            File.WriteAllText(Path.Combine(hotUpdateDirectory, HybridClrManifest.FileName), JsonMapper.ToJson(manifest));
        }

        /// <summary>只解析本次复制的 DLL 资源，不受编辑器已加载程序集或遗留 .dll 文件影响。</summary>
        private sealed class CopiedAssemblyResolver : IAssemblyResolver
        {
            private readonly string _directory;
            internal CopiedAssemblyResolver(string directory) => _directory = directory;
            public string ResolveAssembly(string assemblyName, bool throwExIfNotFind)
            {
                var path = Path.Combine(_directory, assemblyName + ".dll.bytes");
                if (File.Exists(path)) return path;
                if (throwExIfNotFind) throw new FileNotFoundException($"本次热更产物缺少程序集 {assemblyName}。", path);
                return null;
            }
        }
    }
}
