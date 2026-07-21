namespace July.Persistence
{
    /// <summary>小型键值存储边界；平台 SDK 可在项目适配层实现。</summary>
    public interface IKeyValueStore
    {
        bool HasKey(string key);
        string GetString(string key);
        void SetString(string key, string value);
        void DeleteKey(string key);
        void Save();
    }
}
