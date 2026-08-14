using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace July.UI
{
    public class UIToggleItem : Selectable, IPointerClickHandler
    {
        [FormerlySerializedAs("m_Normal")]
        [SerializeField] private GameObject _normal;
        [FormerlySerializedAs("m_Selected")]
        [SerializeField] private GameObject _selected;
        [FormerlySerializedAs("m_Locked")]
        [SerializeField] private GameObject _locked;

        private UIToggleGroup _group;
        private bool _isOn;
        [FormerlySerializedAs("m_IsLocked")]
        [SerializeField] private bool _isLocked;

        public bool IsOn => _isOn;
        public bool IsLocked => _isLocked;

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button > 0) return;
            if (!IsActive() || !IsInteractable()) return;
            if (_group == null) return;

            if (_isLocked)
                _group.NotifyLockedItemClicked(this);
            else
                _group.NotifyItemClicked(this);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _group = GetComponentInParent<UIToggleGroup>();
            UpdateVisuals();
        }

        internal void SetOn(bool value)
        {
            if (_isOn == value) return;
            _isOn = value;
            UpdateVisuals();
        }

        internal void SetLocked(bool value)
        {
            if (_isLocked == value) return;
            _isLocked = value;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (_isLocked)
            {
                if (_normal != null) _normal.SetActive(false);
                if (_selected != null) _selected.SetActive(false);
                if (_locked != null) _locked.SetActive(true);
            }
            else
            {
                if (_normal != null) _normal.SetActive(!_isOn);
                if (_selected != null) _selected.SetActive(_isOn);
                if (_locked != null) _locked.SetActive(false);
            }
        }
    }
}
