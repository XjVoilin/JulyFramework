using System;
using System.IO;
using System.Linq;
namespace July.Release.Editor
{
    public static class PlatformBuildPaths
    {
        public static string GetExportDirectory(BuildContext context) => Path.GetFullPath(
            Path.Combine(ReleaseConventions.ExportRoot, context.Platform, context.CoreVersion));

        // Only the exact requested platform/version export directory may be removed.
        public static void CleanExportDirectory(string exportRoot, string platform, string coreVersion)
        {
            if (!July.Release.PlatformKeys.Options.Contains(platform) || !BuildUtils.IsValidVersion(coreVersion))
                throw new ArgumentException("Invalid export platform/version");
            var root = Path.GetFullPath(exportRoot);
            var target = Path.GetFullPath(Path.Combine(root, platform, coreVersion));
            if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Export directory escapes its configured root");
            // A junction in the export tree could redirect cleanup into another directory.
            for (var parent = new DirectoryInfo(target); parent != null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Export directory contains a link: {parent.FullName}");
            if (!Directory.Exists(target)) return;
            RequireNoLinks(new DirectoryInfo(target));
            Directory.Delete(target, true);
        }

        static void RequireNoLinks(DirectoryInfo directory)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Export directory contains a link: {entry.FullName}");
                if (entry is DirectoryInfo child) RequireNoLinks(child);
            }
        }

        public static PlatformBuildArtifacts Locate(BuildContext context, string exportDirectory, string webglDirectory = null)
        {
            var gameJs = new[] { "minigame/game.js", "tt-minigame/game.js", "game.js" }
                .Select(path => Path.Combine(exportDirectory, path)).FirstOrDefault(File.Exists);
            string dataFile = null;
            if (webglDirectory != null && Directory.Exists(webglDirectory))
            {
                var files = Directory.GetFiles(webglDirectory, "*.webgl.data.unityweb.bin.br", SearchOption.AllDirectories);
                dataFile = files.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (files.Length > 1)
                    UnityEngine.Debug.LogWarning($"[Release] Multiple data files found; using the newest: {dataFile}");
            }
            return new PlatformBuildArtifacts(exportDirectory, gameJs, dataFile);
        }
    }
}
