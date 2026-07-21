using System.Collections.Generic;

namespace July.Platform
{
    public class LaunchOptions
    {
        public bool IsColdStart { get; }
        public string SceneId { get; }
        public IReadOnlyDictionary<string, string> Query { get; }
        public IReadOnlyDictionary<string, string> ExtraData { get; }

        public LaunchOptions(
            bool isColdStart,
            string sceneId,
            IReadOnlyDictionary<string, string> query,
            IReadOnlyDictionary<string, string> extraData = null)
        {
            IsColdStart = isColdStart;
            SceneId = sceneId ?? "";
            Query = query ?? EmptyDict;
            ExtraData = extraData ?? EmptyDict;
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyDict =
            new Dictionary<string, string>();

        public bool Has(string key) => Query.ContainsKey(key);

        public bool TryGetString(string key, out string value)
            => Query.TryGetValue(key, out value);

        public bool TryGetInt(string key, out int value)
        {
            if (Query.TryGetValue(key, out var s) && int.TryParse(s, out value))
                return true;
            value = 0;
            return false;
        }

        public bool SceneEquals(string scene) => SceneId == scene;

        public override string ToString()
        {
            var queryStr = Query.Count > 0 ? string.Join(", ", Query) : "empty";
            return $"[LaunchOptions] cold={IsColdStart}, scene={SceneId}, query=[{queryStr}]";
        }
    }
}
