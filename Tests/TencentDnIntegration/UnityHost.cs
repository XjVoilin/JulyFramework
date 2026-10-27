using System;
using System.Reflection;

namespace UnityEditor { public sealed class InitializeOnLoadMethodAttribute : Attribute { } }
namespace UnityEditor.PackageManager
{
    public sealed class PackageInfo
    {
        public string resolvedPath;
        public static string PackageRoot;
        public static PackageInfo FindForAssembly(Assembly _) => new() { resolvedPath = PackageRoot };
    }
}
namespace UnityEngine { public static class Debug { public static void Log(object _) { } } }
namespace July.Release
{
    public interface IReleaseBootConfig { }
    public static class PlatformKeys
    {
        public const string WeChat = "WeChat";
        public const string TikTok = "TikTok";
    }
}
namespace July.Release.Editor
{
    public sealed class BuildContext { public string Platform; }
    public sealed class PlatformBuildArtifacts
    {
        public string GameJsPath;
        public PlatformBuildArtifacts(string gameJsPath) => GameJsPath = gameJsPath;
    }
    public static class ReleaseProject
    {
        public static ReleaseExportProcessorRegistry ExportProcessors { get; } = new();
    }
}
