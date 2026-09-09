using System;
using System.IO;
using System.Linq;
namespace July.Release.Editor
{
    public static class PlatformBuildPaths
    {
        public static string GetExportDirectory(BuildContext context) => Path.GetFullPath(
            Path.Combine(ReleaseProject.Profile.ExportRoot, context.Platform, context.CoreVersion));

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
