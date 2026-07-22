namespace July.Persistence
{
    /// <summary>ByteGame synchronous storage adapter for TikTok mini-games.</summary>
    public sealed class TikTokPreferencesAdapter : IPlatformPreferencesAdapter
    {
        public string GetString(string key, string defaultValue = "") =>
            TTSDK.TTStorage.GetStringSync(key, defaultValue);

        public void SetString(string key, string value) =>
            TTSDK.TTStorage.SetStringSync(key, value);

        public int GetInt(string key, int defaultValue = 0) =>
            TTSDK.TTStorage.GetIntSync(key, defaultValue);

        public void SetInt(string key, int value) =>
            TTSDK.TTStorage.SetIntSync(key, value);

        public float GetFloat(string key, float defaultValue = 0f) =>
            TTSDK.TTStorage.GetFloatSync(key, defaultValue);

        public void SetFloat(string key, float value) =>
            TTSDK.TTStorage.SetFloatSync(key, value);

        public bool HasKey(string key) => TTSDK.TTStorage.HasKeySync(key);
        public void DeleteKey(string key) => TTSDK.TTStorage.DeleteKeySync(key);
        public void DeleteAll() => TTSDK.TTStorage.DeleteAllSync();

        // TTStorage operations are synchronous and need no explicit flush.
        public void Save() { }
    }
}
