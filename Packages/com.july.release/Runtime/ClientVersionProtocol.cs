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
        public static bool TryReadPrefetch(string json, string requestUrl, string environment,
            string platform, string coreVersion, out string responseJson)
        {
            responseJson = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            JsonData cached;
            try { cached = JsonMapper.ToObject(json); }
            catch (JsonException) { return false; }
            if (cached == null || !cached.IsObject) return false;
            foreach (var key in new[] { "requestUrl", "environment", "platform", "coreVersion", "responseJson" })
                if (!cached.ContainsKey(key) || cached[key] == null || !cached[key].IsString) return false;
            if ((string)cached["requestUrl"] != requestUrl || (string)cached["environment"] != environment ||
                (string)cached["platform"] != platform || (string)cached["coreVersion"] != coreVersion) return false;
            responseJson = (string)cached["responseJson"];
            return !string.IsNullOrWhiteSpace(responseJson);
        }

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

