using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace July.UI
{
    public class UIToggleButton : Selectable, IPointerClickHandler
    {
        [FormerlySerializedAs("m_IsOn")]
        [SerializeField] private bool _isOn;
        [FormerlySerializedAs("m_Normal")]
        [SerializeField] private GameObject _normal;
        [FormerlySerializedAs("m_Selected")]
        [SerializeField] private GameObject _selected;
        [FormerlySerializedAs("m_OnValueChanged")]
        [SerializeField] private Toggle.ToggleEvent _onValueChanged = new();

        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value) return;
                _isOn = value;
                UpdateVisuals();
                _onValueChanged.Invoke(_isOn);
            }
        }

        public Toggle.ToggleEvent OnValueChanged => _onValueChanged;

        public void SetWithoutNotify(bool value)
        {
            if (_isOn == value) return;
            _isOn = value;
            UpdateVisuals();
        }

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button > 0) return;
            if (!IsActive() || !IsInteractable()) return;
            IsOn = !IsOn;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (_normal != null) _normal.SetActive(!_isOn);
            if (_selected != null) _selected.SetActive(_isOn);
        }
    }
}
