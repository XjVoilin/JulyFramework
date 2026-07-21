using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using July.Arch;
using July.Logging;
using UnityEngine;

namespace July.Localization
{
    public class LocalizationSystem : SystemBase, ILocalizationSystem
    {
        private const string FallbackLanguage = "CN";

        private string _currentLanguage = FallbackLanguage;
        private string _defaultLanguage = FallbackLanguage;
        private readonly List<string> _supportedLanguages = new();
        private readonly Dictionary<string, Dictionary<string, string>> _mainLanguageData = new();
        private readonly Dictionary<string, Dictionary<string, string>> _additionalLanguageData = new();

        protected override UniTask OnInitializeAsync()
        {
            if (MainProvider == null)
                JLogger.LogWarning("[LocalizationSystem] MainProvider 未设置。请在 PreInitializeAsync 中调用 SetMainProvider 配置本地化数据源。");
            return UniTask.CompletedTask;
        }

        #region ISupportMultipleSource

        public ILocalizationDataProvider MainProvider { get; private set; }
        public ILocalizationDataProvider AdditionalProvider { get; private set; }

        public void SetMainProvider(ILocalizationDataProvider provider)
        {
            MainProvider = provider;
            _mainLanguageData.Clear();

            if (provider == null) return;

            _defaultLanguage = provider.DefaultLanguage;
            _supportedLanguages.Clear();
            if (provider.SupportedLanguages != null)
            {
                foreach (var code in provider.SupportedLanguages)
                {
                    if (!string.IsNullOrEmpty(code) && !_supportedLanguages.Contains(code))
                        _supportedLanguages.Add(code);
                }
            }
            if (!string.IsNullOrEmpty(_defaultLanguage) && !_supportedLanguages.Contains(_defaultLanguage))
                _supportedLanguages.Add(_defaultLanguage);

            var detected = DetectSystemLanguage();
            _currentLanguage = _supportedLanguages.Contains(detected) ? detected : _defaultLanguage;

            LoadFromProvider(MainProvider, _mainLanguageData, _currentLanguage);
            if (_currentLanguage != _defaultLanguage)
                LoadFromProvider(MainProvider, _mainLanguageData, _defaultLanguage);
        }

        public void SetAdditionalProvider(ILocalizationDataProvider provider)
        {
            AdditionalProvider = provider;
            _additionalLanguageData.Clear();

            if (provider == null) return;

            LoadFromProvider(provider, _additionalLanguageData, _currentLanguage);
            if (_currentLanguage != _defaultLanguage)
                LoadFromProvider(provider, _additionalLanguageData, _defaultLanguage);
        }

        public void UnsetAdditionalProvider(ILocalizationDataProvider provider)
        {
            if (AdditionalProvider == provider)
            {
                AdditionalProvider = null;
                _additionalLanguageData.Clear();
            }
        }

        #endregion

        #region ILocalizationSystem

        public string CurrentLanguage => _currentLanguage;
        public string DefaultLanguage => _defaultLanguage;
        public IReadOnlyList<string> SupportedLanguages => _supportedLanguages;

        public UniTask LoadCurrentLanguageAsync(CancellationToken ct = default)
        {
            return LoadLanguageAsync(_currentLanguage, ct).AsUniTask();
        }

        public async UniTask<bool> SetLanguageAsync(string languageCode, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(languageCode)) return false;
            if (languageCode == _currentLanguage) return true;
            if (!_supportedLanguages.Contains(languageCode)) return false;

            if (!_mainLanguageData.ContainsKey(languageCode))
            {
                var success = await LoadLanguageAsync(languageCode, ct);
                if (!success) return false;
            }

            _currentLanguage = languageCode;
            return true;
        }

        public UniTask<bool> LoadLanguageAsync(string languageCode, CancellationToken ct = default)
        {
            var mainOk = LoadFromProvider(MainProvider, _mainLanguageData, languageCode);
            LoadFromProvider(AdditionalProvider, _additionalLanguageData, languageCode);
            return UniTask.FromResult(mainOk);
        }

        public bool IsLanguageLoaded(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return false;
            return _mainLanguageData.ContainsKey(languageCode);
        }

        public void UnloadLanguage(string languageCode)
        {
            if (languageCode == _currentLanguage)
            {
                JLogger.LogWarning("[LocalizationSystem] Cannot unload current language");
                return;
            }
            _mainLanguageData.Remove(languageCode);
            _additionalLanguageData.Remove(languageCode);
        }

        public string Get(string key, string defaultValue = null)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue ?? string.Empty;

            if (_additionalLanguageData.TryGetValue(_currentLanguage, out var addData) &&
                addData.TryGetValue(key, out var addText))
                return addText;

            if (_mainLanguageData.TryGetValue(_currentLanguage, out var data) &&
                data.TryGetValue(key, out var text))
                return text;

            if (_currentLanguage != _defaultLanguage)
            {
                if (_additionalLanguageData.TryGetValue(_defaultLanguage, out var addFb) &&
                    addFb.TryGetValue(key, out var addFbText))
                    return addFbText;

                if (_mainLanguageData.TryGetValue(_defaultLanguage, out var fallback) &&
                    fallback.TryGetValue(key, out var fbText))
                    return fbText;
            }

            return defaultValue ?? key;
        }

        public string GetFormat(string key, params object[] args)
        {
            var text = Get(key);
            if (args == null || args.Length == 0) return text;
            try { return string.Format(text, args); }
            catch (FormatException) { return text; }
        }

        public bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            if (_additionalLanguageData.TryGetValue(_currentLanguage, out var addData) && addData.ContainsKey(key))
                return true;
            if (_mainLanguageData.TryGetValue(_currentLanguage, out var data) && data.ContainsKey(key))
                return true;

            if (_currentLanguage != _defaultLanguage)
            {
                if (_additionalLanguageData.TryGetValue(_defaultLanguage, out var addFb) && addFb.ContainsKey(key))
                    return true;
                if (_mainLanguageData.TryGetValue(_defaultLanguage, out var fallback) && fallback.ContainsKey(key))
                    return true;
            }

            return false;
        }

        public IReadOnlyList<string> GetAllKeys(string languageCode = null)
        {
            var code = languageCode ?? _currentLanguage;
            var keys = new HashSet<string>();

            if (_mainLanguageData.TryGetValue(code, out var mainData))
                foreach (var k in mainData.Keys) keys.Add(k);
            if (_additionalLanguageData.TryGetValue(code, out var addData))
                foreach (var k in addData.Keys) keys.Add(k);

            return new List<string>(keys);
        }

        #endregion

        #region Internal

        private static bool LoadFromProvider(
            ILocalizationDataProvider provider,
            Dictionary<string, Dictionary<string, string>> cache,
            string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return false;
            if (cache.ContainsKey(languageCode)) return true;
            if (provider == null) return false;
            var dict = provider.LoadLanguage(languageCode);
            if (dict == null) return false;
            cache[languageCode] = dict;
            return true;
        }

        private string DetectSystemLanguage()
        {
            return Application.systemLanguage switch
            {
                SystemLanguage.Chinese => "CN",
                SystemLanguage.ChineseSimplified => "CN",
                SystemLanguage.ChineseTraditional => "TW",
                SystemLanguage.English => "US",
                SystemLanguage.Japanese => "JP",
                SystemLanguage.Korean => "KR",
                SystemLanguage.French => "FR",
                SystemLanguage.German => "DE",
                SystemLanguage.Spanish => "ES",
                SystemLanguage.Portuguese => "BR",
                SystemLanguage.Russian => "RU",
                _ => _defaultLanguage
            };
        }

        protected override void OnShutdown()
        {
            MainProvider = null;
            AdditionalProvider = null;
            _mainLanguageData.Clear();
            _additionalLanguageData.Clear();
        }

        #endregion
    }
}
