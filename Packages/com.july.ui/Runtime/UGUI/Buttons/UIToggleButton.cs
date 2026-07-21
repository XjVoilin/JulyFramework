using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace July.UI
{
    public class UIToggleButton : Selectable, IPointerClickHandler
    {
        [SerializeField] private bool m_IsOn;
        [SerializeField] private GameObject m_Normal;
        [SerializeField] private GameObject m_Selected;
        [SerializeField] private Toggle.ToggleEvent m_OnValueChanged = new();

        public bool IsOn
        {
            get => m_IsOn;
            set
            {
                if (m_IsOn == value) return;
                m_IsOn = value;
                UpdateVisuals();
                m_OnValueChanged.Invoke(m_IsOn);
            }
        }

        public Toggle.ToggleEvent OnValueChanged => m_OnValueChanged;

        public void SetWithoutNotify(bool value)
        {
            if (m_IsOn == value) return;
            m_IsOn = value;
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
            if (m_Normal != null) m_Normal.SetActive(!m_IsOn);
            if (m_Selected != null) m_Selected.SetActive(m_IsOn);
        }
    }
}
