using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace July.Release.Editor
{
    internal static class HybridClrAssemblyManifest
    {
        [Serializable]
        internal sealed class Manifest
        {
            public string[] hotUpdateAssemblies;
            public string[] aotMetadataAssemblies;
        }

        internal static void WriteForProject()
        {
            var profile = ReleaseProject.Profile.HybridCLR;
            Write(profile.HotUpdateDllDirectory, profile.AotMetadataDirectory);
            AssetDatabase.Refresh();
        }

        internal static void Write(string hotUpdateDirectory, string metadataDirectory)
        {
            var hotNames = Names(hotUpdateDirectory);
            if (hotNames.Length == 0) throw new InvalidOperationException("没有生成的热更 DLL，无法写入程序集清单。");
            var manifest = new Manifest { hotUpdateAssemblies = hotNames, aotMetadataAssemblies = Names(metadataDirectory) };
            File.WriteAllText(Path.Combine(hotUpdateDirectory, "hybridclr-manifest.json"), JsonUtility.ToJson(manifest, true));
        }

        static string[] Names(string directory) => Directory.GetFiles(directory, "*.dll.bytes")
            .Select(path => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)))
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
}
