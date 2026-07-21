using System;
using System.Collections.Generic;
using UnityEngine;

namespace July.UI
{
    public class UIToggleGroup : MonoBehaviour
    {
        [SerializeField] private int m_SelectedIndex;
        [SerializeField] private List<UIToggleItem> m_Items = new();

        public event Action<int> OnValueChanged;
        public event Action<int> OnLockedItemClicked;

        public int SelectedIndex
        {
            get => m_SelectedIndex;
            set
            {
                if (value < 0 || value >= m_Items.Count) return;
                if (m_SelectedIndex == value) return;
                if (m_Items[value] != null && m_Items[value].IsLocked) return;
                ApplySelection(value);
                OnValueChanged?.Invoke(m_SelectedIndex);
            }
        }

        public int Count => m_Items.Count;

        public UIToggleItem GetItem(int index) => m_Items[index];

        public bool IsItemLocked(int index)
        {
            if (index < 0 || index >= m_Items.Count) return false;
            return m_Items[index] != null && m_Items[index].IsLocked;
        }

        public void SetItemLocked(int index, bool locked)
        {
            if (index < 0 || index >= m_Items.Count) return;
            if (m_Items[index] != null)
                m_Items[index].SetLocked(locked);
        }

        public void SetWithoutNotify(int index)
        {
            if (index < 0 || index >= m_Items.Count) return;
            if (m_SelectedIndex == index) return;
            ApplySelection(index);
        }

        internal void NotifyItemClicked(UIToggleItem item)
        {
            int index = m_Items.IndexOf(item);
            if (index < 0 || m_SelectedIndex == index) return;
            ApplySelection(index);
            OnValueChanged?.Invoke(m_SelectedIndex);
        }

        internal void NotifyLockedItemClicked(UIToggleItem item)
        {
            int index = m_Items.IndexOf(item);
            if (index < 0) return;
            OnLockedItemClicked?.Invoke(index);
        }

        private void ApplySelection(int index)
        {
            for (int i = 0; i < m_Items.Count; i++)
            {
                if (m_Items[i] != null)
                    m_Items[i].SetOn(i == index);
            }
            m_SelectedIndex = index;
        }

        private void OnEnable()
        {
            if (m_Items.Count > 0)
            {
                m_SelectedIndex = Mathf.Clamp(m_SelectedIndex, 0, m_Items.Count - 1);
                ApplySelection(m_SelectedIndex);
            }
        }
    }
}
