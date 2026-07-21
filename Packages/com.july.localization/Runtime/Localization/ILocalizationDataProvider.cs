using System.Collections.Generic;

namespace July.Localization
{
    /// <summary>
    /// 本地化数据源 —— 一个 LocalizationSystem 实例绑一个。
    /// 项目侧实现：盒子 = LubanLocalizationProvider；小游戏 = GameXXXLocalizationProvider（Generated）。
    /// </summary>
    public interface ILocalizationDataProvider
    {
        string DefaultLanguage { get; }
        IReadOnlyList<string> SupportedLanguages { get; }

        /// <summary>
        /// 按语言码加载文本字典。返回 null 表示该语言无数据。
        /// </summary>
        Dictionary<string, string> LoadLanguage(string languageCode);
    }
}
