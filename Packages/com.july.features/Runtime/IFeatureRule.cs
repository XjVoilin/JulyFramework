namespace July.Features
{
    /// <summary>使用配置参数和当前业务状态判断一项功能条件。</summary>
    public interface IFeatureRule
    {
        /// <summary>注册规则的整数编号，与条件配置中的 type 匹配。</summary>
        int Id { get; }

        /// <summary>查询时按需解析参数并判断条件是否满足。非法参数应抛出 System.ArgumentException。</summary>
        bool IsSatisfied(string arg);
    }
}
