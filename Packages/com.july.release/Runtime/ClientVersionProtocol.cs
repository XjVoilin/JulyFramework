using System;
using LitJson;
namespace July.Release
{
    public enum ReleaseEnvironment { Dev, Test, Prod }
    public static class ClientVersionProtocol
    {
        public const string Path = "/client_version";
        public const string CoreVersionField = "CoreVersion";
        public const string PlanVersionField = "PlanVersion";
        public static string RequestJson(string coreVersion) => new JsonData { [CoreVersionField] = coreVersion }.ToJson();
        public static string ReadPlanVersion(JsonData root, string platform) =>
            root.GetObject("platforms")?.GetObject(platform)?.GetString(PlanVersionField, null);
    }
    public static class ResourceUrls
    {
        public static string CoreRoot(string root, string environment, string platform, string coreVersion) =>
            $"{root.TrimEnd('/')}/{environment}/{platform}/{coreVersion}";
        public static string PlanRoot(string root, string environment, string platform, string coreVersion, string planVersion) =>
            $"{CoreRoot(root, environment, platform, coreVersion)}/{planVersion}";
    }
}

