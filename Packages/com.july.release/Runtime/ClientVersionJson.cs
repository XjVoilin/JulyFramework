using LitJson;

namespace July.Release
{
    internal static class ClientVersionJson
    {
        public static string GetString(this JsonData data, string key, string fallback = "")
        {
            if (data == null || !data.ContainsKey(key)) return fallback;
            return (string)data[key];
        }

        public static bool GetBool(this JsonData data, string key, bool fallback = false)
        {
            if (data == null || !data.ContainsKey(key)) return fallback;
            return (bool)data[key];
        }

        /// <summary>
        /// 安全获取子对象。key 不存在或值不是 object 时返回 null。
        /// </summary>
        public static JsonData GetObject(this JsonData data, string key)
        {
            if (data == null || !data.ContainsKey(key)) return null;
            var value = data[key];
            return value != null && value.IsObject ? value : null;
        }
    }
}

