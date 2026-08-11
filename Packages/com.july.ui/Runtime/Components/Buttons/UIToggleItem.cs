using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace July.UI
{
    public class UIToggleItem : Selectable, IPointerClickHandler
    {
        [SerializeField] private GameObject m_Normal;
        [SerializeField] private GameObject m_Selected;
        [SerializeField] private GameObject m_Locked;

        private UIToggleGroup m_Group;
        private bool m_IsOn;
        [SerializeField]private bool m_IsLocked;

        public bool IsOn => m_IsOn;
        public bool IsLocked => m_IsLocked;

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button > 0) return;
            if (!IsActive() || !IsInteractable()) return;
            if (m_Group == null) return;

            if (m_IsLocked)
                m_Group.NotifyLockedItemClicked(this);
            else
                m_Group.NotifyItemClicked(this);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            m_Group = GetComponentInParent<UIToggleGroup>();
            UpdateVisuals();
        }

        internal void SetOn(bool value)
        {
            if (m_IsOn == value) return;
            m_IsOn = value;
            UpdateVisuals();
        }

        internal void SetLocked(bool value)
        {
            if (m_IsLocked == value) return;
            m_IsLocked = value;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (m_IsLocked)
            {
                if (m_Normal != null) m_Normal.SetActive(false);
                if (m_Selected != null) m_Selected.SetActive(false);
                if (m_Locked != null) m_Locked.SetActive(true);
            }
            else
            {
                if (m_Normal != null) m_Normal.SetActive(!m_IsOn);
                if (m_Selected != null) m_Selected.SetActive(m_IsOn);
                if (m_Locked != null) m_Locked.SetActive(false);
            }
        }
    }
}
