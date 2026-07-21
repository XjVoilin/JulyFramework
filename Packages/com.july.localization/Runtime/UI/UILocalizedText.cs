using July.Arch;
using TMPro;
using UnityEngine;

namespace July.Localization
{
    /// <summary>
    /// 挂在带有 TMP 的物体上，填入 Key 即可自动本地化。
    /// 支持格式化参数，语言切换时自动刷新。
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class UILocalizedText : ArchBehaviour
    {
        [SerializeField] private string key;

        private TextMeshProUGUI _text;
        private object[] _args;

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            Apply();
        }

        public void SetKey(string newKey)
        {
            key = newKey;
            _args = null;
            Apply();
        }

        public void SetKey(string newKey, params object[] args)
        {
            key = newKey;
            _args = args is { Length: > 0 } ? args : null;
            Apply();
        }

        private void Apply()
        {
            if (_text == null || string.IsNullOrEmpty(key)) return;

            var loc = this.GetSystem<ILocalizationSystem>();
            if (loc == null) return;

            _text.text = _args != null
                ? loc.GetFormat(key, _args)
                : loc.Get(key);
        }
    }
}
