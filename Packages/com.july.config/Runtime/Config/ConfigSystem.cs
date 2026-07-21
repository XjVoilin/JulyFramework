using July.Arch;
using July.Logging;
using UnityEngine;

namespace July.Config
{
    /// <summary>
    /// 配置系统 — 通过 MainProvider / AdditionalProvider 两级查询提供类型安全的配置表访问。
    /// AdditionalProvider 优先，MainProvider 兜底。
    /// </summary>
    public class ConfigSystem : SystemBase, IConfigSystem
    {
        public IConfigProvider MainProvider { get; private set; }
        public IConfigProvider AdditionalProvider { get; private set; }

        public void SetMainProvider(IConfigProvider provider) => MainProvider = provider;
        public void SetAdditionalProvider(IConfigProvider provider) => AdditionalProvider = provider;

        public void UnsetAdditionalProvider(IConfigProvider provider)
        {
            if (AdditionalProvider == provider) AdditionalProvider = null;
        }

        public T GetTable<T>() where T : class
        {
            if (AdditionalProvider != null && AdditionalProvider.TryGetTable<T>(out var t))
                return t;
            if (MainProvider != null && MainProvider.TryGetTable<T>(out t))
                return t;
            JLogger.LogError($"[ConfigSystem] 配置表 {typeof(T).Name} 未找到");
            return null;
        }

        public bool TryGetTable<T>(out T table) where T : class
        {
            if (AdditionalProvider != null && AdditionalProvider.TryGetTable<T>(out table))
                return true;
            if (MainProvider != null && MainProvider.TryGetTable<T>(out table))
                return true;
            table = null;
            return false;
        }

        protected override void OnShutdown()
        {
            MainProvider = null;
            AdditionalProvider = null;
        }
    }
}
