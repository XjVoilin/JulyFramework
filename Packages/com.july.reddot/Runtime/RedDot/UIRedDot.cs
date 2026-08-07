using July.Arch;
using July.Logging;
using TMPro;
using UnityEngine;

namespace July.RedDot
{
    /// <summary>
    /// 红点组件（Prefab 自包含）。
    /// 使用方式：将红点 Prefab 拖到目标节点下 → Inspector 选 Key → 完成。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIRedDot : ArchBehaviour
    {
        private const string NumberOverflow = "99+";

        [Header("红点配置")]
        [SerializeField] private string _key;

        [Header("视觉根（Prefab 内拖好，使用时无需修改）")]
        [SerializeField] private GameObject _visualNormal;
        [SerializeField] private GameObject _visualNumber;
        [SerializeField] private GameObject _visualNew;

        [Header("文案")]
        [SerializeField] private TMP_Text _numberText;

        private int _cachedCount = -1;
        private GameObject _activeVisual;
        private string _lastMissingKey;

        public bool IsVisible => _activeVisual != null && _activeVisual.activeSelf;
        public string Key => _key;

        private void OnEnable()
        {
            Rebind();
        }

        private void OnDisable()
        {
            this.UnsubscribeAll();
        }

        public void SetKey(string key)
        {
            if (_key == key)
            {
                if (isActiveAndEnabled)
                    Refresh();
                return;
            }

            _key = key;

            if (string.IsNullOrEmpty(_key))
            {
                this.UnsubscribeAll();
                HideAll();
                return;
            }

            if (isActiveAndEnabled)
                Rebind();
        }

        public void Refresh()
        {
            if (string.IsNullOrEmpty(_key))
            {
                HideAll();
                return;
            }

            var system = this.GetSystem<RedDotSystemBase>();
            if (system == null)
            {
                HideAll();
                return;
            }

            var node = system.GetNode(_key);
            if (node == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (_lastMissingKey != _key)
                    JLogger.LogWarning($"[UIRedDot] Runtime key「{_key}」未注册。请从 Inspector 重新选择完整红点路径。", this);
#endif
                _lastMissingKey = _key;
                HideAll();
                return;
            }

            _lastMissingKey = null;
            var type = node.Type;
            var count = system.GetCount(_key);
            Present(type, count);
        }

        private void Present(RedDotType type, int count)
        {
            if (count <= 0)
            {
                HideAll();
                return;
            }

            var visual = type switch
            {
                RedDotType.Normal => _visualNormal,
                RedDotType.Number => _visualNumber,
                RedDotType.New => _visualNew,
                _ => _visualNormal
            };

            if (visual == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                JLogger.LogWarning($"[UIRedDot] Key「{_key}」类型 {type}，Prefab 中缺少对应视觉根。", this);
#endif
                HideAll();
                return;
            }

            SetAllVisualsActive(false);
            visual.SetActive(true);
            _activeVisual = visual;

            if (type == RedDotType.Number)
            {
                if (count != _cachedCount)
                {
                    _cachedCount = count;
                    if (_numberText != null)
                        _numberText.text = count > 99 ? NumberOverflow : count.ToString();
                }
            }
            else
            {
                _cachedCount = -1;
            }
        }

        private void HideAll()
        {
            SetAllVisualsActive(false);
            _activeVisual = null;
            _cachedCount = -1;
        }

        private void SetAllVisualsActive(bool active)
        {
            if (_visualNormal != null) _visualNormal.SetActive(active);
            if (_visualNumber != null) _visualNumber.SetActive(active);
            if (_visualNew != null) _visualNew.SetActive(active);
        }

        private void OnRedDotChanged(RedDotChangedEvent evt)
        {
            Refresh();
        }

        private void Rebind()
        {
            this.UnsubscribeAll();

            if (string.IsNullOrEmpty(_key))
            {
                HideAll();
                return;
            }

            var system = this.GetSystem<RedDotSystemBase>();
            system?.OnKeyChanged(_key, OnRedDotChanged, this);
            Refresh();
        }
    }
}
