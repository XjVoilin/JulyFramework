namespace July.Features
{
    public interface IFeatureSystem
    {
        /// <summary>解析、校验并替换功能配置。失败时保留上一份有效配置。</summary>
        void LoadConfig(string json);

        /// <summary>查询已配置功能的当前可用性。未配置的功能不可用。</summary>
        bool CanUse(int featureId);
    }
}
