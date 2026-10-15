using July.Arch;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace July.Localization
{
    /// <summary>
    /// 挂在带有 TextMeshProUGUI 或 TextMeshPro 的物体上，填入 Key 即可自动本地化。
    /// 支持格式化参数，语言切换时自动刷新。
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class UILocalizedText : ArchBehaviour
    {
        [FormerlySerializedAs("key")]
        [SerializeField] private string _key;

        private TMP_Text _text;
        private object[] _args;

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            Apply();
        }

        public void SetKey(string newKey)
        {
            _key = newKey;
            _args = null;
            Apply();
        }

        public void SetKey(string newKey, params object[] args)
        {
            _key = newKey;
            _args = args is { Length: > 0 } ? args : null;
            Apply();
        }

        private void Apply()
        {
            if (_text == null || string.IsNullOrEmpty(_key)) return;

            var loc = this.GetSystem<ILocalizationSystem>();
            if (loc == null) return;

            _text.text = _args != null
                ? loc.GetFormat(_key, _args)
                : loc.Get(_key);
        }
    }
}
